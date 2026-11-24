import { useCallback, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { formatRelativeTime, searchEventInfo } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import { Badge } from '../../components/Badges.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import SnippetText from '../../components/forums/SnippetText.jsx';
import { authorName, errorText, webPathFor } from '../../utils/engagement.js';
import { isoToLocalInput, localInputToIso } from '../../utils/time.js';
import { useDirectoryTerms } from '../../utils/phase4.js';
import { featureEnabled } from './memberRoutes.js';
import { useProperty } from '../../context/PropertyContext.jsx';

const s = strings.search;
/** Content types offered as filters; results of other types still render. */
export const SEARCH_TYPES = Object.freeze(['FeedPost', 'Thread', 'Comment', 'User']);

/** Feature-dependent types: offered only when the property turns the feature on. */
export const FEATURE_SEARCH_TYPES = Object.freeze([['events', 'Event'], ['library', 'Series'], ['library', 'Lesson'], ['directory', 'Venue'], ['directory', 'Instructor'], ['ideas', 'Idea']]);

/** The type filters for a property: the core types plus those of every enabled feature. */
export function searchTypesFor(property) {
  return SEARCH_TYPES.concat(FEATURE_SEARCH_TYPES.filter(([feature]) => featureEnabled(property, feature)).map(([, type]) => type));
}

function Results({ client, meta, types, from, to }) {
  const typeKey = types.join(',');
  const fetchPage = useCallback(
    (cursor, signal) => client.searchContent({ ...meta, ordering: undefined, createdAfterUtc: from || undefined, createdBeforeUtc: to || undefined, continuationToken: cursor, maxResults: 20, filters: typeKey ? { types: typeKey.split(',') } : {} }, signal),
    [client, meta, typeKey, from, to]
  );
  const list = useCursorList(fetchPage);
  const { d } = useDirectoryTerms();
  return (
    <>
      <ErrorBanner message={list.error ? s.error + ' ' + errorText(list.error, {}, '') : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="search" title={s.empty} />}
      <ol className="search-results">
        {list.items.map((hit) => {
          const to = webPathFor(hit.path);
          const title = hit.title || (hit.type === 'User' && hit.author ? authorName(hit.author) : s.untitled);
          const ev = searchEventInfo(hit);
          return (
            <li key={hit.type + ':' + hit.id} className="search-result">
              <Badge tone="neutral">{s.typeBadge[hit.type] ? d(s.typeBadge[hit.type]) : hit.type}</Badge>
              {ev && ev.recurring && <> <Badge tone="info">{s.recurring}</Badge></>}
              {ev && ev.canceled && <> <Badge tone="danger">{s.canceled}</Badge></>}
              <h2 className="search-result-title">{to ? <Link to={to}>{title}</Link> : title}</h2>
              {ev && (
                <p className="muted small search-result-event">
                  {ev.recurring && !ev.past && !ev.canceled ? s.nextDate(ev.when) : s.eventDate(ev.when)}
                  {ev.recurrenceSummary && <span> · {ev.recurrenceSummary}</span>}
                  {ev.occurrenceCount > 1 && <span> · {s.occurrences(ev.occurrenceCount)}</span>}
                </p>
              )}
              <SnippetText snippet={hit.snippet} />
              <p className="muted small">
                {hit.author && hit.type !== 'User' && <span>{strings.forums.by(authorName(hit.author))}</span>}
                {hit.forum && <span> · {strings.forums.inForum(hit.forum.name)}</span>}
                {hit.createdUtc && <span> · {formatRelativeTime(hit.createdUtc)}</span>}
              </p>
            </li>
          );
        })}
      </ol>
      {list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{s.loadMore}</button>}
    </>
  );
}

/**
 * /app/search (resolved decision 11: global search in Phase 3): one search box across every
 * content type, with type, label, tag, and date filters, all kept in the URL. Results come from
 * `POST /search/enumerate` as an EnumerationResult with highlighted snippets.
 */
function SearchView() {
  const { client } = useAuth();
  const { property } = useProperty();
  const { d } = useDirectoryTerms();
  const typeOptions = searchTypesFor(property);
  useDocumentMeta(s.meta, s.summary);
  const [searchParams, setSearchParams] = useSearchParams();
  const [meta, setMeta] = useEnumerationUrlState();
  const [text, setText] = useState(meta.search);
  const [lastSearch, setLastSearch] = useState(meta.search);
  if (lastSearch !== meta.search) {
    setLastSearch(meta.search);
    setText(meta.search);
  }
  const types = searchParams.getAll('types').filter(Boolean);
  const from = searchParams.get('from') || '';
  const to = searchParams.get('to') || '';

  const setParam = (key, values) => {
    const next = new URLSearchParams(searchParams);
    next.delete(key);
    for (const v of [].concat(values)) if (v) next.append(key, v);
    setSearchParams(next);
  };
  const toggleType = (type) => setParam('types', types.includes(type) ? types.filter((x) => x !== type) : types.concat(type));

  return (
    <div className="member-view search-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{s.title}</h1>
          <p className="muted">{s.summary}</p>
        </div>
      </header>
      <form className="search-form" role="search" onSubmit={(e) => { e.preventDefault(); setMeta({ ...meta, search: text.trim() }); }}>
        <label className="visually-hidden" htmlFor="global-search">{s.label}</label>
        <input id="global-search" type="search" value={text} onChange={(e) => setText(e.target.value)} placeholder={s.placeholder} maxLength={200} />
        <button type="submit" className="button button-primary">{s.submit}</button>
      </form>
      <div className="chip-row" role="group" aria-label={s.typesLabel}>
        {typeOptions.map((type) => (
          <button key={type} type="button" className={'chip-toggle' + (types.includes(type) ? ' active' : '')} aria-pressed={types.includes(type)} onClick={() => toggleType(type)}>{d(s.types[type])}</button>
        ))}
      </div>
      <div className="composer-options">
        <div className="field field-compact">
          <label className="label-text" htmlFor="search-from">{s.from}</label>
          <input id="search-from" type="datetime-local" value={from ? isoToLocalInput(from) : ''} onChange={(e) => setParam('from', e.target.value ? localInputToIso(e.target.value) : '')} />
        </div>
        <div className="field field-compact">
          <label className="label-text" htmlFor="search-to">{s.to}</label>
          <input id="search-to" type="datetime-local" value={to ? isoToLocalInput(to) : ''} onChange={(e) => setParam('to', e.target.value ? localInputToIso(e.target.value) : '')} />
        </div>
      </div>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} />
      {meta.search ? <Results client={client} meta={meta} types={types} from={from} to={to} /> : <p className="muted">{s.prompt}</p>}
    </div>
  );
}

export default SearchView;

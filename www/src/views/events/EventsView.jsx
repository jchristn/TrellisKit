import { useCallback, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { EventFormats, eventCategories, eventsConfig, monthWindowUtc, roundCoordinate, webMapStyle } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import useInfiniteScroll from '../../hooks/useInfiniteScroll.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import Icon from '../../components/Icon.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { EventRow, MonthCalendar } from '../../components/events/EventParts.jsx';
import SearchMap from '../../components/maps/SearchMap.jsx';
import MapSearchLayout, { MapResultRows, useMapSearch } from '../../components/maps/MapSearchLayout.jsx';
import PlaceSearch, { locateMe } from '../../components/maps/PlaceSearch.jsx';
import { errorText } from '../../utils/engagement.js';
import { appPaths, phase4Errors, useBoundedList } from '../../utils/phase4.js';

const t = strings.events;
const PAGE_SIZE = 25;

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const RSVP_STATUSES = Object.freeze(['Going', 'Interested', 'Waitlisted', 'None']);

function nextDay(date) {
  const d = new Date(date + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10) + 'T00:00:00Z';
}

/**
 * Event filters from the URL (`when`, `category`, `format`, `spots`, `rsvp`, `from` and `to` dates
 * (YYYY-MM-DD, inclusive), and `online`, which adds online-only events to a map search's list).
 */
export function eventFiltersFrom(params) {
  const filters = { when: ['Upcoming', 'Past', 'All'].includes(params.get('when')) ? params.get('when') : 'Upcoming' };
  const categories = params.getAll('category').filter(Boolean);
  if (categories.length) filters.category = categories;
  const formats = params.getAll('format').filter((x) => EventFormats.includes(x));
  if (formats.length) filters.format = formats;
  if (params.get('spots') === 'true') filters.hasSpots = true;
  if (params.get('rsvp')) filters.rsvpStatus = [params.get('rsvp')];
  if (DATE.test(params.get('from') || '')) filters.startsAfterUtc = params.get('from') + 'T00:00:00Z';
  if (DATE.test(params.get('to') || '')) filters.startsBeforeUtc = nextDay(params.get('to'));
  if (params.get('online') === 'true') filters.includeOnline = true;
  return filters;
}

/** The advanced event filters of the map search: when, dates, category, format, spots, RSVP, and online. */
function EventFilterControls({ searchParams, filters, write, toggle, categories, signedIn }) {
  return (
    <>
      <div className="p4-filters">
        <label className="field field-compact">
          <span className="label-text">{t.whenLabel}</span>
          <select className="select-compact" value={filters.when} onChange={(e) => write({ when: e.target.value === 'Upcoming' ? '' : e.target.value })}>
            {Object.keys(t.when).map((w) => <option key={w} value={w}>{t.when[w]}</option>)}
          </select>
        </label>
        <label className="field field-compact">
          <span className="label-text">{t.dateFrom}</span>
          <input type="date" className="input-compact" value={searchParams.get('from') || ''} onChange={(e) => write({ from: e.target.value })} />
        </label>
        <label className="field field-compact">
          <span className="label-text">{t.dateTo}</span>
          <input type="date" className="input-compact" value={searchParams.get('to') || ''} onChange={(e) => write({ to: e.target.value })} />
        </label>
        {signedIn && (
          <label className="field field-compact">
            <span className="label-text">{t.rsvpLabel}</span>
            <select className="select-compact" value={searchParams.get('rsvp') || ''} onChange={(e) => write({ rsvp: e.target.value })}>
              <option value="">{t.rsvpAny}</option>
              {RSVP_STATUSES.map((s) => <option key={s} value={s}>{t.rsvpStatuses[s]}</option>)}
            </select>
          </label>
        )}
        <label className="check-field">
          <input type="checkbox" checked={searchParams.get('spots') === 'true'} onChange={(e) => write({ spots: e.target.checked ? 'true' : '' })} />
          <span>{t.hasSpots}</span>
        </label>
        <label className="check-field">
          <input type="checkbox" checked={Boolean(filters.includeOnline)} onChange={(e) => write({ online: e.target.checked ? 'true' : '' })} aria-describedby="events-online-hint" />
          <span>{t.includeOnline}</span>
        </label>
      </div>
      <p id="events-online-hint" className="muted small">{t.includeOnlineHint}</p>
      {categories.length > 0 && (
        <div className="chip-row" role="group" aria-label={t.categoryLabel}>
          {categories.map((c) => {
            const on = (filters.category || []).includes(c.key);
            return <button key={c.key} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} onClick={() => toggle('category', c.key)}><span className="p4-chip-dot" style={c.color ? { background: c.color } : undefined} aria-hidden="true" />{c.label}</button>;
          })}
        </div>
      )}
      <div className="chip-row" role="group" aria-label={t.formatLabel}>
        {EventFormats.map((f) => {
          const on = (filters.format || []).includes(f);
          return <button key={f} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} onClick={() => toggle('format', f)}>{t.formats[f]}</button>;
        })}
      </div>
    </>
  );
}

/**
 * The map-first event search: in-person and hybrid events on the map (online-only events have no
 * place, so they join the list only with "Include online events"), "Search this area" re-queries
 * the list for the visible box, rows and markers highlight each other, and the advanced pane holds
 * the search, labels, tags, place lookup, "Use my location", and every event filter.
 */
function EventMapSearch({ client, property, meta, setMeta, filters, searchParams, write, toggle, categories, signedIn, onFailed, toolbar }) {
  const search = useMapSearch('events', property);
  const [hovered, setHovered] = useState(null);
  const [selected, setSelected] = useState(null);
  const [sheet, setSheet] = useState(false);
  const [locError, setLocError] = useState('');
  const { includeOnline: _online, ...mapFilters } = filters;
  const listKey = JSON.stringify({ ...filters, bbox: search.bbox });
  const mapKey = JSON.stringify(mapFilters);
  const fetchPage = useCallback((cursor, signal) => {
    const f = JSON.parse(listKey);
    return client.enumerateEvents({ ...meta, ordering: meta.search ? 'Relevance' : f.when === 'Past' ? 'StartDescending' : undefined, continuationToken: cursor, maxResults: PAGE_SIZE, filters: f }, signal);
  }, [client, meta, listKey]);
  const list = useCursorList(fetchPage, { enabled: Boolean(search.bbox) });
  const fetchFeatures = useCallback((q, signal) => client.enumerateEventMap(q, signal), [client]);
  const mapQuery = useMemo(() => ({ labels: meta.labels, labelMatch: meta.labelMatch, tags: meta.tags, search: meta.search, filters: JSON.parse(mapKey) }), [mapKey, meta]);
  const count = Object.keys(filters).length - 1 + (filters.when !== 'Upcoming' ? 1 : 0) + meta.labels.length + Object.keys(meta.tags).length + (meta.search ? 1 : 0);
  const advanced = (
    <>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Event" showSearch searchLabel={t.searchLabel} />
      <div className="p4-filters">
        <PlaceSearch client={client} onPick={(c) => search.goTo({ bbox: c.bbox || null, lat: Number(c.lat), lng: Number(c.lng), zoom: 11 })} />
        <button type="button" className="button button-secondary button-sm" onClick={() => { setLocError(''); locateMe((lat, lng) => search.goTo({ lat: roundCoordinate(lat, 2), lng: roundCoordinate(lng, 2), zoom: 11 }), setLocError); }}><Icon name="pin" size={14} /><span>{strings.venues.useLocation}</span></button>
      </div>
      <p className="muted small">{strings.venues.locationHint}</p>
      {locError && <Banner tone="warning">{locError}</Banner>}
      <EventFilterControls searchParams={searchParams} filters={filters} write={write} toggle={toggle} categories={categories} signedIn={signedIn} />
    </>
  );
  return (
    <MapSearchLayout
      testId="event-search"
      advanced={advanced}
      advancedLabel={t.advancedSearch}
      advancedCount={count}
      toolbar={toolbar}
      showSearchArea={search.showSearchArea}
      onSearchArea={search.searchArea}
      searchAreaLabel={t.searchThisArea}
      resultsLabel={t.resultsTitle}
      resultsSummary={search.bbox ? t.resultsInArea(list.items.length, list.loaded && !list.done) : strings.venues.mapSearching}
      sheetOpen={sheet}
      onSheet={setSheet}
      map={(
        <SearchMap
          property={property}
          kind="events"
          fetchFeatures={fetchFeatures}
          query={mapQuery}
          viewport={search.viewport}
          focus={search.focus}
          onReady={search.onReady}
          onMove={search.onMove}
          hoveredId={hovered}
          selectedId={selected}
          onHover={setHovered}
          onSelect={(id) => { setSelected(id); setSheet(true); }}
          onFailed={onFailed}
          label={t.mapTitle}
          testId="event-map"
        />
      )}
      results={(
        <>
          <ErrorBanner message={list.error ? t.loadError + ' ' + errorText(list.error, phase4Errors(), '') : ''} onRetry={list.items.length ? list.loadMore : list.refresh} />
          {!list.loaded && list.loading && <Loading />}
          {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="calendar" title={t.emptyArea} />}
          <MapResultRows items={list.items} hoveredId={hovered} selectedId={selected} onHover={setHovered} renderRow={(e) => <EventRow event={e} />} />
          {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{t.loadMore}</button>}
          {list.loaded && list.done && list.items.length > 0 && <p className="muted small feed-end">{t.end}</p>}
        </>
      )}
    />
  );
}

function EventList({ client, meta, filters }) {
  const key = JSON.stringify(filters);
  const fetchPage = useCallback((cursor, signal) => {
    const f = JSON.parse(key);
    return client.enumerateEvents({ ...meta, ordering: meta.search ? 'Relevance' : f.when === 'Past' ? 'StartDescending' : undefined, continuationToken: cursor, maxResults: PAGE_SIZE, filters: f }, signal);
  }, [client, meta, key]);
  const list = useCursorList(fetchPage);
  const sentinel = useInfiniteScroll(list.loadMore, { enabled: list.loaded && !list.done && !list.error });
  const filtered = Boolean(meta.search || meta.labels.length || Object.keys(meta.tags).length || Object.keys(filters).length > 1);
  return (
    <section className="p4-section" aria-label={t.title}>
      <ErrorBanner message={list.error ? t.loadError + ' ' + errorText(list.error, phase4Errors(), '') : ''} onRetry={list.items.length ? list.loadMore : list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="calendar" title={filtered ? t.emptyFiltered : filters.when === 'Past' ? t.emptyPast : t.empty} />}
      <div className="p4-list">{list.items.map((e) => <EventRow key={e.id} event={e} />)}</div>
      <div ref={sentinel} className="feed-sentinel" aria-hidden="true" />
      {list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{t.loadMore}</button>}
      {list.loaded && list.done && list.items.length > 0 && <p className="muted small feed-end">{t.end}</p>}
    </section>
  );
}

function CalendarView({ client, meta, filters, year, month, onMonth }) {
  const range = monthWindowUtc(year, month);
  const key = JSON.stringify({ ...filters, when: 'All', ...range });
  const fetchPage = useCallback((page, signal) => client.enumerateEvents({ labels: meta.labels, labelMatch: meta.labelMatch, tags: meta.tags, search: meta.search, ...page, filters: JSON.parse(key) }, signal), [client, meta, key]);
  // A month holds a bounded number of events: at most 2 pages of 100 are loaded.
  const list = useBoundedList(fetchPage, { pageSize: 100, maxPages: 2 });
  const move = (delta) => {
    const d = new Date(Date.UTC(year, month - 1 + delta, 1));
    onMonth(d.getUTCFullYear(), d.getUTCMonth() + 1);
  };
  const now = new Date();
  return (
    <>
      <ErrorBanner message={list.error ? t.loadError + ' ' + errorText(list.error, phase4Errors(), '') : ''} onRetry={list.reload} />
      {list.loading && <Loading inline />}
      <MonthCalendar year={year} month={month} events={list.items} truncated={list.truncated} onPrev={() => move(-1)} onNext={() => move(1)} onToday={() => onMonth(now.getFullYear(), now.getMonth() + 1)} />
    </>
  );
}

/** Calendar subscription settings (section 4.5): show, copy, rotate, or turn off the private feed URL. */
export function CalendarFeedPanel({ client }) {
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [confirm, setConfirm] = useState(null);
  const [busy, setBusy] = useState(false);
  const fetcher = useCallback((signal) => client.getCalendarFeed(signal), [client]);
  const query = useApiQuery(fetcher, { enabled: open });
  const [feed, setFeed] = useState(null);
  const current = feed || query.data;
  const run = async (kind) => {
    setBusy(true);
    try {
      if (kind === 'rotate') {
        setFeed(await client.rotateCalendarFeed());
        toast.success(t.feedRotated);
      } else {
        await client.deleteCalendarFeed();
        setFeed(null);
        setOpen(false);
        toast.success(t.feedDeleted);
      }
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    } finally {
      setBusy(false);
      setConfirm(null);
    }
  };
  return (
    <section className="panel p4-section" aria-labelledby="events-feed">
      <h2 id="events-feed" className="section-title">{t.feedTitle}</h2>
      <p className="muted small">{t.feedBody} {t.feedNote}</p>
      {!open && <div><button type="button" className="button button-secondary button-sm" onClick={() => setOpen(true)}><Icon name="calendar" size={14} /><span>{t.feedShow}</span></button></div>}
      {open && query.loading && !current && <Loading inline />}
      {open && query.error && <ErrorBanner message={errorText(query.error, phase4Errors(), t.feedDisabled)} />}
      {open && current && (
        <>
          <label className="field">
            <span className="label-text">{t.feedUrl}</span>
            <span className="input-with-button">
              <input readOnly value={current.url} onFocus={(e) => e.target.select()} data-testid="calendar-feed-url" />
              <CopyButton value={current.url} label={t.feedCopy} />
            </span>
          </label>
          <div className="form-actions-start">
            <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={() => setConfirm('rotate')}>{t.feedRotate}</button>
            <button type="button" className="button button-danger-outline button-sm" disabled={busy} onClick={() => setConfirm('delete')}>{t.feedDelete}</button>
          </div>
        </>
      )}
      <ConfirmModal
        open={Boolean(confirm)}
        title={confirm === 'rotate' ? t.feedRotate : t.feedDelete}
        message={confirm === 'rotate' ? t.feedRotateConfirm : t.feedDeleteConfirm}
        confirmLabel={confirm === 'rotate' ? t.feedRotate : t.feedDelete}
        busy={busy}
        onCancel={() => setConfirm(null)}
        onConfirm={() => run(confirm)}
      />
    </section>
  );
}

/**
 * /app/events (docs/PHASE4_CONTRACT.md sections 4.1, 4.2, 16): with a map style the default view is
 * the map-first search (EventMapSearch); the list and the month calendar stay as the other views,
 * and are the only ones without a map style or when the map fails. When, dates, category, format,
 * spots, my RSVP, online, search, label, and tag filters, the viewport (`at`), and the searched area
 * (`bbox`) live in the URL. Offers "Propose an event" when proposals are on, my proposals, and the
 * calendar feed settings.
 */
function EventsView() {
  const { client, me } = useAuth();
  const { property, fill } = useProperty();
  const [searchParams, setSearchParams] = useSearchParams();
  const [meta, setMeta] = useEnumerationUrlState();
  const [mapFailed, setMapFailed] = useState(false);
  useDocumentMeta(strings.member.metaTitle(t.title));
  const config = eventsConfig(property);
  const categories = eventCategories(property);
  const filters = useMemo(() => eventFiltersFrom(searchParams), [searchParams]);
  const hasMap = Boolean(webMapStyle(property)) && !mapFailed;
  const views = hasMap ? ['map', 'list', 'calendar'] : ['list', 'calendar'];
  const defaultView = views[0];
  const view = views.includes(searchParams.get('view')) ? searchParams.get('view') : defaultView;
  const now = new Date();
  const year = Number(searchParams.get('y')) || now.getFullYear();
  const month = Math.min(12, Math.max(1, Number(searchParams.get('m')) || now.getMonth() + 1));

  const write = (changes) => {
    const next = new URLSearchParams(searchParams);
    for (const [k, v] of Object.entries(changes)) {
      next.delete(k);
      for (const value of Array.isArray(v) ? v : [v]) if (value) next.append(k, value);
    }
    setSearchParams(next);
  };
  const toggle = (key, value) => {
    const current = searchParams.getAll(key);
    write({ [key]: current.includes(value) ? current.filter((x) => x !== value) : current.concat(value) });
  };

  return (
    <div className="member-view events-view">
      <PageHeader
        title={t.title}
        summary={fill(t.summary)}
        actions={me && (
          <>
            {config.proposals && <Link to={appPaths.proposeEvent} className="button button-primary button-sm"><Icon name="plus" size={14} /><span>{t.propose}</span></Link>}
            {config.proposals && <Link to={appPaths.myProposals} className="button button-secondary button-sm">{t.myProposals}</Link>}
          </>
        )}
      />
      <div className="segmented" role="radiogroup" aria-label={t.viewToggle}>
        {views.map((v) => <button key={v} type="button" role="radio" aria-checked={view === v} className={'segment' + (view === v ? ' active' : '')} onClick={() => write({ view: v === defaultView ? '' : v })}>{v === 'map' ? t.mapView : v === 'list' ? t.listView : t.calendarView}</button>)}
      </div>
      {mapFailed && <Banner tone="info">{strings.venues.mapFailed}</Banner>}
      {view === 'map' ? (
        <EventMapSearch client={client} property={property} meta={meta} setMeta={setMeta} filters={filters} searchParams={searchParams} write={write} toggle={toggle} categories={categories} signedIn={Boolean(me)} onFailed={() => setMapFailed(true)} />
      ) : (
      <>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Event" showSearch searchLabel={t.searchLabel} />
      <div className="p4-filters">
        {view === 'list' && (
          <label className="field field-compact">
            <span className="label-text">{t.whenLabel}</span>
            <select className="select-compact" value={filters.when} onChange={(e) => write({ when: e.target.value === 'Upcoming' ? '' : e.target.value })}>
              {Object.keys(t.when).map((w) => <option key={w} value={w}>{t.when[w]}</option>)}
            </select>
          </label>
        )}
        {me && (
          <label className="check-field">
            <input type="checkbox" checked={searchParams.get('rsvp') === 'Going'} onChange={(e) => write({ rsvp: e.target.checked ? 'Going' : '' })} />
            <span>{t.goingOnly}</span>
          </label>
        )}
        <label className="check-field">
          <input type="checkbox" checked={searchParams.get('spots') === 'true'} onChange={(e) => write({ spots: e.target.checked ? 'true' : '' })} />
          <span>{t.hasSpots}</span>
        </label>
      </div>
      {categories.length > 0 && (
        <div className="chip-row" role="group" aria-label={t.categoryLabel}>
          {categories.map((c) => {
            const on = (filters.category || []).includes(c.key);
            return <button key={c.key} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} onClick={() => toggle('category', c.key)}><span className="p4-chip-dot" style={c.color ? { background: c.color } : undefined} aria-hidden="true" />{c.label}</button>;
          })}
        </div>
      )}
      <div className="chip-row" role="group" aria-label={t.formatLabel}>
        {EventFormats.map((f) => {
          const on = (filters.format || []).includes(f);
          return <button key={f} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} onClick={() => toggle('format', f)}>{t.formats[f]}</button>;
        })}
      </div>
      {view === 'list'
        ? <EventList client={client} meta={meta} filters={filters} />
        : <CalendarView client={client} meta={meta} filters={filters} year={year} month={month} onMonth={(y, m) => write({ y: String(y), m: String(m) })} />}
      </>
      )}
      {me && config.calendarFeedEnabled && <CalendarFeedPanel client={client} />}
    </div>
  );
}

export default EventsView;

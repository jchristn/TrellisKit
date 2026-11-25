import { useCallback, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { VisitorPolicies, directoryConfig, formatAddress, formatDistance, isLockedItem, programLabels, programOptions, roundCoordinate, webMapStyle } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { LockBadge } from '../../components/AccessPrompt.jsx';
import VenueMap from '../../components/directory/VenueMap.jsx';
import VenuePreviewModal from '../../components/directory/VenuePreviewModal.jsx';
import { RatingBadge } from '../../components/directory/Stars.jsx';
import MapSearchLayout, { MapResultRows, useMapSearch } from '../../components/maps/MapSearchLayout.jsx';
import PlaceSearch, { locateMe } from '../../components/maps/PlaceSearch.jsx';
import { errorText } from '../../utils/engagement.js';
import { appPaths, mediaThumbUrl, phase4Errors, useDirectoryTerms } from '../../utils/phase4.js';

const v = strings.venues;
const PAGE_SIZE = 25;
const RADII = Object.freeze([5, 10, 25, 50, 100, 250]);

/** Venue filters from the URL: `program`, `visitor`, `verified`, `closed`, `lat`, `lng`, `r`, and a `bbox` (`s,w,n,e`). */
export function venueFiltersFrom(params) {
  const filters = {};
  const programs = params.getAll('program').filter(Boolean);
  if (programs.length) filters.programs = programs;
  const visitor = params.getAll('visitor').filter((x) => VisitorPolicies.includes(x));
  if (visitor.length) filters.visitorPolicy = visitor;
  if (params.get('verified') === 'true') filters.verifiedOnly = true;
  if (params.get('closed') === 'true') filters.includeClosed = true;
  const lat = Number(params.get('lat'));
  const lng = Number(params.get('lng'));
  if (params.get('lat') && params.get('lng') && Number.isFinite(lat) && Number.isFinite(lng)) {
    filters.near = { lat: roundCoordinate(lat, 2), lng: roundCoordinate(lng, 2), radiusKm: Number(params.get('r')) || 50 };
  } else if (params.get('bbox')) {
    const [south, west, north, east] = params.get('bbox').split(',').map(Number);
    if ([south, west, north, east].every(Number.isFinite)) filters.bbox = { south, west, north, east };
  }
  return filters;
}

/** One venue in the list (VenueSummary). */
export function VenueRow({ venue }) {
  const { property } = useProperty();
  const { d } = useDirectoryTerms();
  const locked = isLockedItem(venue);
  const thumb = mediaThumbUrl(venue.coverPhoto);
  return (
    <Link to={appPaths.venue(venue.slug || venue.id)} className="p4-row" data-testid={'venue-row-' + venue.id}>
      {thumb ? <img src={thumb} alt="" width={48} height={48} style={{ borderRadius: 6, objectFit: 'cover' }} /> : <Icon name="pin" size={20} />}
      <span className="p4-row-main">
        <span className="p4-row-title">{venue.name}</span>
        <span className="p4-card-meta">
          <span>{formatAddress(venue.address, venue.addressVisibility, { includeCountry: false })}</span>
          {venue.ratingCount > 0 && <RatingBadge average={venue.ratingAverage} count={venue.ratingCount} />}
          {venue.distanceKm !== null && venue.distanceKm !== undefined && <span>{v.distance(formatDistance(venue.distanceKm))}</span>}
          {venue.instructorCount > 0 && <span>{d(v.instructorCount(venue.instructorCount))}</span>}
          {venue.isVerified && <Badge tone="success">{v.verified}</Badge>}
          {venue.status === 'Closed' && <Badge tone="danger">{v.closed}</Badge>}
          {locked && <LockBadge feature="directory" />}
        </span>
        {(venue.programs || []).length > 0 && <span className="muted small">{programLabels(property, venue.programs).join(', ')}</span>}
      </span>
      <Icon name="chevronRight" size={16} />
    </Link>
  );
}

/** The venue filters both layouts share: visitor policy, verified, closed, and programs. */
function VenueFilterControls({ filters, write, toggle, programs }) {
  return (
    <>
      <div className="p4-filters">
        <label className="field field-compact">
          <span className="label-text">{v.visitorPolicyLabel}</span>
          <select className="select-compact" value={(filters.visitorPolicy || [''])[0]} onChange={(e) => write({ visitor: e.target.value })}>
            <option value="">{v.anyVisitorPolicy}</option>
            {VisitorPolicies.map((p) => <option key={p} value={p}>{v.visitorPolicies[p]}</option>)}
          </select>
        </label>
        <label className="check-field"><input type="checkbox" checked={Boolean(filters.verifiedOnly)} onChange={(e) => write({ verified: e.target.checked ? 'true' : '' })} /><span>{v.verifiedOnly}</span></label>
        <label className="check-field"><input type="checkbox" checked={Boolean(filters.includeClosed)} onChange={(e) => write({ closed: e.target.checked ? 'true' : '' })} /><span>{v.includeClosed}</span></label>
      </div>
      {programs.length > 0 && (
        <div className="chip-row" role="group" aria-label={v.programsLabel}>
          {programs.map((p) => {
            const on = (filters.programs || []).includes(p.key);
            return <button key={p.key} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} onClick={() => toggle('program', p.key)}>{p.label}</button>;
          })}
        </div>
      )}
    </>
  );
}

function VenueResults({ list, filtered, empty, d, rowsFor }) {
  return (
    <>
      <ErrorBanner message={list.error ? d(v.loadError) + ' ' + errorText(list.error, phase4Errors(), '') : ''} onRetry={list.items.length ? list.loadMore : list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="pin" title={d(empty || (filtered ? v.emptyFiltered : v.empty))} />}
      {rowsFor(list.items)}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{v.loadMore}</button>}
      {list.loaded && list.done && list.items.length > 0 && <p className="muted small feed-end">{v.end}</p>}
    </>
  );
}

/**
 * The map-first venue search: the map fills the page, "Search this area" re-queries the list for
 * the visible box, rows and markers highlight each other, and the advanced pane holds the text,
 * label, and tag search, the place lookup and "Use my location" (both move the map), and the
 * filters.
 */
function VenueMapSearch({ client, property, meta, setMeta, filters, write, toggle, programs, onFailed, onListOnly }) {
  const { d } = useDirectoryTerms();
  const search = useMapSearch('venues', property);
  const [hovered, setHovered] = useState(null);
  const [selected, setSelected] = useState(null);
  const [sheet, setSheet] = useState(false);
  const [preview, setPreview] = useState(null);
  const [locError, setLocError] = useState('');
  const { near: _near, bbox: _bbox, ...rest } = filters;
  const restKey = JSON.stringify(rest);
  const bboxKey = JSON.stringify(search.bbox);
  const fetchPage = useCallback((cursor, signal) => client.enumerateVenues({ ...meta, ordering: meta.search ? 'Relevance' : undefined, continuationToken: cursor, maxResults: PAGE_SIZE, filters: { ...JSON.parse(restKey), bbox: JSON.parse(bboxKey) } }, signal), [client, meta, restKey, bboxKey]);
  const list = useCursorList(fetchPage, { enabled: Boolean(search.bbox) });
  const mapQuery = useMemo(() => ({ labels: meta.labels, labelMatch: meta.labelMatch, tags: meta.tags, search: meta.search, filters: JSON.parse(restKey) }), [restKey, meta]);
  const count = Object.keys(rest).length + meta.labels.length + Object.keys(meta.tags).length + (meta.search ? 1 : 0);
  // Picking a place on the map opens its preview; the list row is highlighted behind it.
  const select = (id) => {
    setSelected(id);
    setPreview(id);
  };
  const advanced = (
    <>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Venue" showSearch searchLabel={d(v.searchLabel)} />
      <div className="p4-filters">
        <PlaceSearch client={client} onPick={(c) => search.goTo({ bbox: c.bbox || null, lat: Number(c.lat), lng: Number(c.lng), zoom: 11 })} />
        <button type="button" className="button button-secondary button-sm" onClick={() => { setLocError(''); locateMe((lat, lng) => search.goTo({ lat: roundCoordinate(lat, 2), lng: roundCoordinate(lng, 2), zoom: 11 }), setLocError); }}><Icon name="pin" size={14} /><span>{v.useLocation}</span></button>
      </div>
      <p className="muted small">{v.locationHint}</p>
      {locError && <Banner tone="warning">{locError}</Banner>}
      <VenueFilterControls filters={filters} write={write} toggle={toggle} programs={programs} />
    </>
  );
  return (
    <>
    <VenuePreviewModal client={client} venueId={preview} onClose={() => setPreview(null)} />
    <MapSearchLayout
      testId="venue-search"
      advanced={advanced}
      advancedLabel={v.advancedSearch}
      advancedCount={count}
      toolbar={<button type="button" className="button button-secondary button-sm" onClick={onListOnly}><Icon name="list" size={14} /><span>{v.showList}</span></button>}
      showSearchArea={search.showSearchArea}
      onSearchArea={search.searchArea}
      searchAreaLabel={v.searchThisArea}
      resultsLabel={v.resultsTitle}
      resultsSummary={search.bbox ? d(v.resultsInArea(list.items.length, list.loaded && !list.done)) : v.mapSearching}
      sheetOpen={sheet}
      onSheet={setSheet}
      map={(
        <VenueMap
          property={property}
          client={client}
          query={mapQuery}
          viewport={search.viewport}
          focus={search.focus}
          onReady={search.onReady}
          onMove={search.onMove}
          hoveredId={hovered}
          selectedId={selected}
          onHover={setHovered}
          onSelect={select}
          onFailed={onFailed}
        />
      )}
      results={(
        <VenueResults
          list={list}
          d={d}
          empty={v.emptyArea}
          rowsFor={(items) => <MapResultRows items={items} hoveredId={hovered} selectedId={selected} onHover={setHovered} renderRow={(venue) => <VenueRow venue={venue} />} />}
        />
      )}
    />
    </>
  );
}

/**
 * /app/directory (docs/PHASE4_CONTRACT.md sections 3 and 16). With a map style the search is map
 * first (VenueMapSearch); without one, when the map fails, or with `map=off`, the venue list with
 * filters, the place search, "Use my location" (rounded to 2 places before it is sent), and the
 * radius. Filters, the viewport (`at`), and the searched area (`bbox`) live in the URL. Offers
 * "Add a {venue}" and the member's own suggestions. All labels come from the property's directory
 * terms.
 */
function DirectoryView() {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const { d } = useDirectoryTerms();
  const [searchParams, setSearchParams] = useSearchParams();
  const [meta, setMeta] = useEnumerationUrlState();
  const [mapFailed, setMapFailed] = useState(false);
  const [locError, setLocError] = useState('');
  useDocumentMeta(strings.member.metaTitle(d(v.title)));
  const config = directoryConfig(property);
  const programs = programOptions(property);
  const filters = useMemo(() => venueFiltersFrom(searchParams), [searchParams]);
  const hasMap = Boolean(webMapStyle(property)) && !mapFailed;
  const mapFirst = hasMap && searchParams.get('map') !== 'off';
  const key = JSON.stringify(filters);

  const write = (changes) => {
    const next = new URLSearchParams(searchParams);
    for (const [k, value] of Object.entries(changes)) {
      next.delete(k);
      for (const item of Array.isArray(value) ? value : [value]) if (item !== '' && item !== null && item !== undefined) next.append(k, String(item));
    }
    setSearchParams(next);
  };
  const toggle = (k, value) => {
    const current = searchParams.getAll(k);
    write({ [k]: current.includes(value) ? current.filter((x) => x !== value) : current.concat(value) });
  };
  const setNear = (lat, lng, label) => write({ lat: roundCoordinate(lat, 2), lng: roundCoordinate(lng, 2), place: label || '', bbox: '' });

  const fetchPage = useCallback((cursor, signal) => client.enumerateVenues({ ...meta, ordering: meta.search ? 'Relevance' : undefined, continuationToken: cursor, maxResults: PAGE_SIZE, filters: JSON.parse(key) }, signal), [client, meta, key]);
  const list = useCursorList(fetchPage, { enabled: !mapFirst });
  const filtered = Boolean(meta.search || meta.labels.length || Object.keys(meta.tags).length || Object.keys(filters).length);
  const nearLabel = filters.near ? (searchParams.get('place') || v.nearMe) : null;

  return (
    <div className="member-view directory-venues-view">
      <PageHeader
        title={d(v.title)}
        summary={config.introduction || d(v.summary)}
        actions={me && (
          <>
            {config.submissions && <Link to={appPaths.submit('NewVenue')} className="button button-primary button-sm"><Icon name="plus" size={14} /><span>{d(v.add)}</span></Link>}
            <Link to={appPaths.mySubmissions} className="button button-secondary button-sm">{v.mySubmissions}</Link>
          </>
        )}
      />
      {mapFailed && <Banner tone="info">{v.mapFailed}</Banner>}
      {mapFirst ? (
        <VenueMapSearch client={client} property={property} meta={meta} setMeta={setMeta} filters={filters} write={write} toggle={toggle} programs={programs} onFailed={() => setMapFailed(true)} onListOnly={() => write({ map: 'off' })} />
      ) : (
        <>
          <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Venue" showSearch searchLabel={d(v.searchLabel)} />
          <div className="p4-filters">
            <PlaceSearch client={client} onPick={(c) => setNear(c.lat, c.lng, c.label)} />
            <button type="button" className="button button-secondary button-sm" onClick={() => { setLocError(''); locateMe((lat, lng) => setNear(lat, lng, ''), setLocError); }}><Icon name="pin" size={14} /><span>{v.useLocation}</span></button>
            {filters.near && (
              <>
                <span className="chip">{v.nearLabel(nearLabel)}<button type="button" className="chip-remove" onClick={() => write({ lat: '', lng: '', place: '', r: '' })} aria-label={v.clearNear}><Icon name="close" size={10} /></button></span>
                <label className="inline-field">
                  <span className="label-text">{v.radius}</span>
                  <select className="select-compact" value={filters.near.radiusKm} onChange={(e) => write({ r: e.target.value })}>
                    {RADII.map((r) => <option key={r} value={r}>{v.radiusValue(r)}</option>)}
                  </select>
                </label>
              </>
            )}
            {filters.bbox && <span className="chip">{v.searchThisArea}<button type="button" className="chip-remove" onClick={() => write({ bbox: '', at: '' })} aria-label={strings.metadata.clearAll}><Icon name="close" size={10} /></button></span>}
            {hasMap && <button type="button" className="button button-secondary button-sm" onClick={() => write({ map: '' })}><Icon name="pin" size={14} /><span>{v.showMap}</span></button>}
          </div>
          <p className="muted small">{v.locationHint}</p>
          {locError && <Banner tone="warning">{locError}</Banner>}
          <VenueFilterControls filters={filters} write={write} toggle={toggle} programs={programs} />
          <section className="p4-section" aria-label={v.listTitle}>
            <VenueResults list={list} filtered={filtered} d={d} rowsFor={(items) => <div className="p4-list">{items.map((venue) => <VenueRow key={venue.id} venue={venue} />)}</div>} />
          </section>
        </>
      )}
      {config.listingNotice && <p className="muted small">{config.listingNotice}</p>}
    </div>
  );
}

export default DirectoryView;

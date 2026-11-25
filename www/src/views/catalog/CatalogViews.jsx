import { useCallback, useMemo } from 'react';
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom';
import { CatalogApparatusFields, CatalogDescriptorFields, catalogDescriptorProfile, catalogDescriptorOptions, CatalogKinds, CatalogDifficulties, CatalogStatuses, PublicPageKind, publicPagePath, publicPagesConfig } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import SaveButton from '../../components/SaveButton.jsx';
import { TechniqueLessons } from '../training/LessonTechniques.jsx';
import { trainingPaths } from '../training/trainingModel.js';

const t = strings.exerciseCatalog;
const filterKeys = ['kind', 'program', 'difficulty', 'muscle', 'equipment', 'familyKey', 'nameLanguage', 'attireKey', 'levelKey', 'notationSystemKey'];
export const catalogPath = (entry) => '/app/catalog/' + encodeURIComponent(entry.slug || entry.id);
export const catalogLabel = (config, group, key) => config?.[group]?.find((item) => item.key === key)?.label || key;
const option = (value, label) => ({ key: value, label });

export function CatalogFilters({ value, onChange, config, admin = false }) {
  const options = {
    kind: CatalogKinds.map((key) => option(key, t.kinds[key])),
    difficulty: CatalogDifficulties.map((key) => option(key, t.difficulties[key])),
    program: config?.programs || [], muscle: config?.muscles || [], equipment: config?.equipment || [],
    ...Object.fromEntries(CatalogDescriptorFields.map(({ key, group }) => [key, catalogDescriptorOptions(config, value.program, group, value[key])])),
    nameLanguage: catalogDescriptorOptions(config, value.program, 'nameLanguages', value.nameLanguage),
    status: CatalogStatuses.map((key) => option(key, t.statuses[key]))
  };
  return <div className="p4-filters">{[...filterKeys, ...(admin ? ['status'] : [])].filter((key) => !['familyKey', 'nameLanguage', 'attireKey', 'levelKey', 'notationSystemKey'].includes(key) || options[key].length).map((key) => <label className="field" key={key}><span className="label-text">{catalogDescriptorProfile(config, value.program)?.[CatalogDescriptorFields.find((field) => field.key === key)?.labelKey] || t[key]}</span><select value={value[key] || ''} onChange={(e) => onChange({ ...value, [key]: e.target.value })}><option value="">{t.all}</option>{options[key].map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}</select></label>)}</div>;
}

export function CatalogListState({ list, emptyText = t.empty }) {
  return <><ErrorBanner message={list.error ? t.error : ''} onRetry={list.items.length ? list.loadMore : list.refresh} />{list.loading && <Loading />}{list.loaded && !list.loading && !list.error && !list.items.length && <p>{emptyText}</p>}{list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary" onClick={list.loadMore}>{t.more}</button>}</>;
}

/**
 * The catalog home (`/app/catalog`, and the Training hub's Techniques segment when `embedded`: the
 * hub supplies the header and the one unified search box, so this view drops its own and ignores
 * `search` while keeping label, tag, and catalog filters).
 * @param {{ embedded?: boolean }} props
 */
export function CatalogHomeView({ embedded = false }) {
  const { client, me } = useAuth();
  const signedIn = Boolean(me);
  const [params, setParams] = useSearchParams();
  const [meta, setMeta, urlMetaKey] = useEnumerationUrlState();
  const metaKey = useMemo(() => (embedded ? JSON.stringify({ ...JSON.parse(urlMetaKey), search: '' }) : urlMetaKey), [embedded, urlMetaKey]);
  const filterKey = JSON.stringify(Object.fromEntries(filterKeys.map((key) => [key, params.get(key) || ''])));
  const filters = useMemo(() => JSON.parse(filterKey), [filterKey]);
  const config = useApiQuery(useCallback((signal) => client.getExerciseCatalogConfig(signal), [client]));
  const fetchPage = useCallback((cursor, signal) => client[signedIn ? 'enumerateCatalogEntries' : 'enumeratePublicCatalogEntries']({ ...JSON.parse(metaKey), filters: JSON.parse(filterKey), maxResults: 25, continuationToken: cursor }, signal), [client, signedIn, metaKey, filterKey]);
  const list = useCursorList(fetchPage);
  useDocumentMeta(embedded ? null : t.title);
  return <div className={embedded ? 'stack' : 'member-view stack'}>{!embedded && <PageHeader title={t.title} summary={t.summary} />}
    {!embedded && <MetadataFilterBar value={meta} onChange={setMeta} client={client} showSearch searchLabel={t.search} />}
    <ErrorBanner message={config.error ? t.error : ''} onRetry={config.reload} />
    <CatalogFilters config={config.data} value={filters} onChange={(next) => setParams((current) => { const updated = new URLSearchParams(current); for (const key of filterKeys) { if (next[key]) updated.set(key, next[key]); else updated.delete(key); } return updated; })} />
    <ul className="p4-list">{list.items.map((entry) => <li key={entry.id} className="panel"><div className="row-between"><h2 className="section-title"><Link to={embedded ? trainingPaths.catalog(entry.slug || entry.id) : catalogPath(entry)}>{entry.name}</Link></h2><SaveButton targetType="CatalogEntry" targetId={entry.id} title={entry.name} compact showOptions={false} /></div><p className="muted small">{[t.kinds[entry.kind], catalogLabel(config.data, 'programs', entry.program), t.difficulties[entry.difficulty]].filter(Boolean).join(' · ')}</p>{entry.summary && <p>{entry.summary}</p>}</li>)}</ul>
    <CatalogListState list={list} />
  </div>;
}

function safeSourceUrl(value) { try { const url = new URL(value); return ['http:', 'https:'].includes(url.protocol) ? url.href : null; } catch { return null; } }

/** Plain structured text does not interpret authored instructions as HTML. */
export function CatalogEntryContent({ entry, config, hideLessonIds = false }) {
  const groups = ['aliases', 'primaryMuscles', 'secondaryMuscles', 'equipment', 'instructions', 'cues', 'commonErrors', 'regressions', 'progressions', 'substitutions', 'contraindications'];
  const label = (key, value) => ['primaryMuscles', 'secondaryMuscles'].includes(key) ? catalogLabel(config, 'muscles', value) : key === 'equipment' ? catalogLabel(config, 'equipment', value) : value;
  return <div className="stack">
    {entry.summary && <p style={{ whiteSpace: 'pre-wrap' }}>{entry.summary}</p>}
    <dl className="p4-kv">{['program', 'movementPattern', 'plane', 'laterality', 'techniqueType', 'fromPosition', 'toPosition'].filter((key) => entry[key]).map((key) => <div key={key}><dt>{t.fields[key]}</dt><dd>{key === 'program' ? catalogLabel(config, 'programs', entry[key]) : entry[key]}</dd></div>)}</dl>
    {CatalogDescriptorFields.filter(({ key }) => entry.modalityDetails?.[key]).map(({ key, group, labelKey }) => <section className="panel" key={key}><h2 className="section-title">{catalogDescriptorProfile(config, entry.program)?.[labelKey] || t[key]}</h2><p>{catalogDescriptorOptions(config, entry.program, group, entry.modalityDetails[key]).find((item) => item.key === entry.modalityDetails[key])?.label}</p>{key === 'levelKey' && <p className="muted small">{t.levelHint}</p>}{key === 'notationSystemKey' && entry.modalityDetails.notationText && <p style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{entry.modalityDetails.notationText}</p>}</section>)}
    {entry.modalityDetails?.localizedNames?.length > 0 && <section className="panel"><h2 className="section-title">{t.localizedNames}</h2><ul style={{ paddingInlineStart: '1.5rem', overflowWrap: 'anywhere' }}>{entry.modalityDetails.localizedNames.map((name, i) => <li key={i}><span lang={name.languageTag}>{name.text}</span>{' · '}{catalogDescriptorOptions(config, entry.program, 'nameLanguages', name.languageTag).find((item) => item.key === name.languageTag)?.label}</li>)}</ul></section>}
    {CatalogApparatusFields.some((key) => entry.apparatusSetup?.[key]) && <section className="panel stack"><h2 className="section-title">{t.apparatus.title}</h2><dl className="stack">{CatalogApparatusFields.filter((key) => entry.apparatusSetup[key]).map((key) => <div key={key}><dt className="label-text">{t.apparatus[key]}</dt><dd style={{ margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{key === 'apparatusKey' ? catalogLabel(config, 'equipment', entry.apparatusSetup[key]) : entry.apparatusSetup[key]}</dd></div>)}</dl>{entry.apparatusSetup.springSetupText && <p className="muted small">{t.apparatus.springHint}</p>}</section>}
    {groups.filter((key) => entry[key]?.length).map((key) => { const List = key === 'instructions' ? 'ol' : 'ul'; return <section className="panel" key={key}><h2 className="section-title">{t.fields[key]}</h2><List style={{ paddingInlineStart: '1.5rem' }}>{entry[key].map((value, i) => <li key={i} style={{ whiteSpace: 'pre-wrap' }}>{label(key, value)}</li>)}</List></section>; })}
    {entry.sources?.length > 0 && <section className="panel"><h2 className="section-title">{t.sources}</h2><ul style={{ paddingInlineStart: '1.5rem' }}>{entry.sources.map((source, i) => <li key={i}>{safeSourceUrl(source.url) ? <a href={safeSourceUrl(source.url)} target="_blank" rel="noreferrer">{source.title || source.url}</a> : <span>{source.title}</span>}{source.license && <p>{source.license}</p>}{source.attribution && <p>{source.attribution}</p>}</li>)}</ul></section>}
    {!hideLessonIds && entry.lessonIds?.length > 0 && <section className="panel"><h2 className="section-title">{t.lessons}</h2><ul style={{ paddingInlineStart: '1.5rem' }}>{entry.lessonIds.map((id, i) => <li key={id}><Link to={'/app/lessons/' + encodeURIComponent(id)}>{t.viewLesson} {i + 1}</Link></li>)}</ul></section>}
  </div>;
}

export function CatalogRelationships({ client, entryId, publicPage = false, admin = false }) {
  const method = admin ? 'adminEnumerateCatalogRelationships' : publicPage ? 'enumeratePublicCatalogRelationships' : 'enumerateCatalogRelationships';
  const list = useCursorList(useCallback((cursor, signal) => client[method](entryId, { maxResults: 25, continuationToken: cursor }, signal), [client, method, entryId]));
  return <section className="panel"><h2 className="section-title">{t.related}</h2><ul style={{ paddingInlineStart: '1.5rem' }}>{list.items.map((edge) => <li key={edge.id}><Link to={admin ? '/admin/catalog/' + encodeURIComponent(edge.targetId) : catalogPath({ id: edge.targetId, slug: edge.targetSlug })}>{edge.targetName || edge.targetId}</Link>{' · '}{t.relationshipKinds[edge.kind] || edge.kind}{edge.note && <p>{edge.note}</p>}</li>)}</ul><CatalogListState list={list} emptyText={t.noRelated} /></section>;
}

export function CatalogEntryView() {
  const { id } = useParams();
  return <CatalogDetail key={id} id={id} />;
}

function CatalogDetail({ id }) {
  const { property } = useProperty();
  const location = useLocation();
  const inHub = location.pathname.startsWith(trainingPaths.overview + '/');
  const { client, me } = useAuth();
  const signedIn = Boolean(me);
  const query = useApiQuery(useCallback((signal) => client[signedIn ? 'getCatalogEntry' : 'getPublicCatalogEntry'](id, signal), [client, signedIn, id]));
  const config = useApiQuery(useCallback((signal) => client.getExerciseCatalogConfig(signal), [client]));
  useDocumentMeta(query.data?.name || t.title);
  const publicPath = publicPagesConfig(property).enabled ? publicPagePath(property, PublicPageKind.Catalog, query.data?.slug) : null;
  return <div className="member-view stack"><Link className="back-link" to={inHub ? trainingPaths.segment('techniques') : '/app/catalog'}>{t.back}</Link><ErrorBanner message={query.error ? t.error : ''} onRetry={query.reload} />{query.loading && <Loading />}{query.data && <><PageHeader title={query.data.name} summary={[t.kinds[query.data.kind], t.difficulties[query.data.difficulty]].filter(Boolean).join(' · ')} actions={<SaveButton targetType="CatalogEntry" targetId={query.data.id} title={query.data.name} />} />{publicPath && <a href={publicPath}>{strings.publicPages.view}</a>}<CatalogEntryContent entry={query.data} config={config.data} hideLessonIds /><TechniqueLessons key={'lessons-' + query.data.id} client={client} entryId={query.data.id} publicPage={!signedIn} /><CatalogRelationships key={query.data.id} client={client} entryId={query.data.id} publicPage={!signedIn} /></>}</div>;
}

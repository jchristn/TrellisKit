import { useCallback, useMemo } from 'react';
import { Navigate, Outlet, useLocation, useOutletContext } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useKillSwitch } from '../../context/AppContentContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import PageHeader from '../../components/PageHeader.jsx';
import SectionTabs from '../../components/training/SectionTabs.jsx';
import { EmptyState, Loading } from '../../components/States.jsx';
import { memberRoutesFor } from '../member/memberRoutes.js';
import { libraryOpen, normalizeOverview, ranksEnabled, sectionOfPath, sectionPath, visibleSections } from './trainingModel.js';

const t = strings.training;

/** Training state shared with the section views: `{ overview, query, guest, sections }`. */
export function useTraining() {
  return useOutletContext() || {};
}

/** Errors after which the hub falls back to the Library alone (no Overview to show). */
function libraryOnly(error) {
  return Boolean(error && [402, 403, 404, 503].includes(error.status));
}

/**
 * /app/training (docs/TRELLISKIT.md, Training hub): the hub header and its section tabs from the
 * overview's `sections` (only `available: true`; unknown keys ignored). One `GET /me/training` per
 * visit feeds every section. Signed-out visitors, and everyone while the `training` kill switch is
 * on, see only the Library, without calling `/me/training`; the Library itself follows the
 * `library` switch and access rules. When the overview cannot be shown (not available, membership
 * required) the Library still works from its own routes.
 */
function TrainingLayout() {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const location = useLocation();
  const killed = useKillSwitch('training').killed;
  const guest = !me;
  const enabled = !guest && !killed;
  const fetcher = useCallback((signal) => client.getTrainingOverview(signal), [client]);
  const query = useApiQuery(fetcher, { enabled });
  const overview = useMemo(() => (enabled && query.data ? normalizeOverview(query.data) : null), [enabled, query.data]);
  const sections = useMemo(() => {
    const library = libraryOpen(property, me) ? ['Library'] : [];
    if (!enabled || libraryOnly(query.error)) return library;
    if (query.error) return ['Overview', ...library];
    if (!overview) return null;
    return visibleSections(overview).filter((key) => key !== 'Programs' || ranksEnabled(property));
  }, [enabled, query.error, overview, property, me]);
  const navLabel = (memberRoutesFor(property).find((route) => route.id === 'training') || {}).label;
  const title = navLabel || t.title;
  useDocumentMeta(strings.member.metaTitle(title));

  if (!sections) return <div className="member-view"><Loading label={t.loading} /></div>;
  if (!sections.length) return <div className="member-view"><EmptyState icon="info" title={t.unavailable} /></div>;
  const current = sectionOfPath(location.pathname);
  if (!sections.includes(current)) return <Navigate to={{ pathname: sectionPath(sections[0]), search: location.search }} replace />;
  const tabs = sections.map((key) => ({ key, label: t.sections[key] || key, to: sectionPath(key) }));
  return (
    <div className="member-view training-view">
      <PageHeader title={title} summary={enabled ? t.summary : null} />
      {tabs.length > 1 && <SectionTabs label={t.sectionsLabel} tabs={tabs} current={current} controls="training-panel" id="training-section" />}
      <div id="training-panel" role={tabs.length > 1 ? 'tabpanel' : undefined} aria-labelledby={tabs.length > 1 ? 'training-section-' + current : undefined} className="training-panel">
        <Outlet context={{ overview, query, guest, sections }} />
      </div>
    </div>
  );
}

export default TrainingLayout;

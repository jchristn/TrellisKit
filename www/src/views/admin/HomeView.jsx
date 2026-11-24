import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import { errorMessage } from '../../utils/api.js';
import { rangeToParams } from '../../utils/time.js';
import { formatDateTime, formatDuration, formatNumber, formatPercent, formatTime, formatUptime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import KpiCard from '../../components/KpiCard.jsx';
import ActivityChart from '../../components/ActivityChart.jsx';
import RangeSelector from '../../components/RangeSelector.jsx';
import ServiceLinks from '../../components/ServiceLinks.jsx';
import Icon from '../../components/Icon.jsx';
import { HealthDot } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import MembershipTiles from './MembershipTiles.jsx';
import { MetricsHomeSummary } from './metrics/MetricsView.jsx';
import { EngagementTiles, ForumTiles, ModerationTiles } from './EngagementTiles.jsx';
import { capabilityAvailable } from '../member/memberRoutes.js';
import { useProperty } from '../../context/PropertyContext.jsx';

const h = strings.admin.home;

/** Home for moderators: the moderation tiles only (contract section 10.2). */
function ModeratorHome() {
  // Staff without a moderation scope (a Communicator only) get no report counts: the queue would answer 403.
  const { staff } = useAdminAuth();
  const moderates = !staff || staff.moderator || staff.supportStaff;
  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.home.label} summary={moderates ? strings.admin.moderation.moderatorOnlyHome : strings.admin.moderation.staffHome} />
      {moderates && <ModerationTiles />}
    </div>
  );
}

/** Admin home: server health, request KPIs, activity chart, quick links, and external services. */
function AdminHome() {
  const { api } = useAdminAuth();
  const forumsOn = capabilityAvailable(useProperty().property, 'forums');
  const navigate = useNavigate();
  const [rangeId, setStoredRange] = useLocalStorage('tk.admin.home.range', 'day');
  const [now, setNow] = useState(() => new Date());
  const params = useMemo(() => rangeToParams(rangeId, now), [rangeId, now]);
  const setRangeId = (id) => {
    setStoredRange(id);
    setNow(new Date());
  };

  const healthFetcher = useCallback((signal) => api.getHealth(signal), [api]);
  const health = useApiQuery(healthFetcher);

  const summaryFetcher = useCallback(
    (signal) => api.getRequestHistorySummary({ fromUtc: params.fromUtc, toUtc: params.toUtc, bucketMinutes: params.bucketMinutes }, signal),
    [api, params]
  );
  const summary = useApiQuery(summaryFetcher);
  const s = summary.data;

  const refresh = () => {
    health.reload();
    setNow(new Date());
  };

  const onBucketClick = (bucket) => {
    const qs = new URLSearchParams({ fromUtc: bucket.bucketStartUtc, toUtc: bucket.bucketEndUtc });
    navigate('/admin/requests?' + qs.toString());
  };

  const healthStatus = health.error ? 'Unreachable' : health.data && health.data.status;
  const healthLabels = { Healthy: strings.admin.topbar.healthy, Degraded: strings.admin.topbar.degraded, Unhealthy: strings.admin.topbar.unhealthy, Unreachable: strings.admin.topbar.unreachable };
  const successRate = s && s.totalCount > 0 ? formatPercent(s.totalSuccess / s.totalCount) : '0.0%';

  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes.home.label}
        summary={strings.admin.routes.home.summary}
        actions={(
          <button type="button" className="button button-secondary button-sm" onClick={refresh} disabled={summary.loading}>
            <Icon name="refresh" size={14} className={summary.loading ? 'spin' : ''} /><span>{strings.common.refresh}</span>
          </button>
        )}
      />

      <section className="panel" aria-labelledby="health-title">
        <div className="panel-header">
          <h2 id="health-title">{h.healthTitle}</h2>
          {healthStatus && <HealthDot status={healthStatus} label={healthLabels[healthStatus] || healthStatus} />}
        </div>
        {health.loading && !health.data && <Loading inline />}
        {health.error && <ErrorBanner message={h.healthError + ' ' + errorMessage(health.error, '')} onRetry={health.reload} />}
        {health.data && (
          <dl className="kv-grid kv-grid-wide">
            <div className="kv-item"><dt>{h.status}</dt><dd>{health.data.status}</dd></div>
            <div className="kv-item"><dt>{h.version}</dt><dd className="mono">{health.data.version}</dd></div>
            <div className="kv-item"><dt>{h.database}</dt><dd>{health.data.database}</dd></div>
            <div className="kv-item"><dt>{h.uptime}</dt><dd>{formatUptime(health.data.uptimeSeconds)}</dd></div>
            <div className="kv-item"><dt>{h.started}</dt><dd title={health.data.startedUtc}>{formatDateTime(health.data.startedUtc)}</dd></div>
          </dl>
        )}
      </section>

      <ModerationTiles />
      <MetricsHomeSummary />
      <MembershipTiles />
      <EngagementTiles />
      {forumsOn && <ForumTiles />}

      <div className="kpi-grid">
        <KpiCard label={h.totalRequests} value={s ? formatNumber(s.totalCount) : '-'} to="/admin/requests" />
        <KpiCard label={h.successes} value={s ? formatNumber(s.totalSuccess) : '-'} tone="success" />
        <KpiCard label={h.failures} value={s ? formatNumber(s.totalFailure) : '-'} tone={s && s.totalFailure > 0 ? 'danger' : undefined} to="/admin/requests" />
        <KpiCard label={h.successRate} value={s ? successRate : '-'} />
        <KpiCard label={h.avgDuration} value={s ? formatDuration(s.averageDurationMs) : '-'} />
      </div>

      <section className="panel" aria-labelledby="activity-title">
        <div className="panel-header">
          <h2 id="activity-title">{h.activityTitle}</h2>
          <div className="panel-actions">
            <RangeSelector value={rangeId} onChange={setRangeId} disabled={summary.loading} />
            <button type="button" className="icon-button" onClick={() => setNow(new Date())} disabled={summary.loading} aria-label={strings.common.refresh} title={strings.common.refresh}>
              <Icon name="refresh" className={summary.loading ? 'spin' : ''} />
            </button>
          </div>
        </div>
        {summary.error && <ErrorBanner message={h.summaryError + ' ' + errorMessage(summary.error, '')} onRetry={summary.reload} />}
        {summary.loading && !s ? <Loading /> : (
          <ActivityChart summary={s} rangeId={rangeId} fromUtc={params.fromUtc} onBucketClick={onBucketClick} />
        )}
        {summary.loadedAt && <p className="muted small">{h.lastLoaded(formatTime(summary.loadedAt))}</p>}
      </section>

      <section aria-labelledby="quick-title">
        <h2 id="quick-title" className="section-title">{h.quickLinksTitle}</h2>
        <div className="cta-grid">
          <Link to="/admin/requests" className="cta-card">
            <Icon name="alert" size={20} />
            <span><strong>{h.quickLinks.failures.title}</strong><span className="muted small">{h.quickLinks.failures.body}</span></span>
          </Link>
          <Link to="/admin/explorer" className="cta-card">
            <Icon name="play" size={20} />
            <span><strong>{h.quickLinks.explorer.title}</strong><span className="muted small">{h.quickLinks.explorer.body}</span></span>
          </Link>
          <Link to="/admin/settings" className="cta-card">
            <Icon name="gear" size={20} />
            <span><strong>{h.quickLinks.settings.title}</strong><span className="muted small">{h.quickLinks.settings.body}</span></span>
          </Link>
        </div>
      </section>

      <section className="panel" aria-labelledby="services-title">
        <div className="panel-header">
          <h2 id="services-title">{h.servicesTitle}</h2>
        </div>
        <p className="muted small">{h.servicesBody}</p>
        <ServiceLinks />
      </section>
    </div>
  );
}

/** /admin/home: the full overview for administrators, moderation tiles for moderators. */
function HomeView() {
  const { isModeratorOnly } = useAdminAuth();
  return isModeratorOnly ? <ModeratorHome /> : <AdminHome />;
}

export default HomeView;

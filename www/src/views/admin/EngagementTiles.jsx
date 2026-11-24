import { loadDailyMetrics } from '../../utils/metricCollections.js';
import { useCallback, useMemo } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { normalizePage } from '../../utils/query.js';
import { formatNumber } from '../../utils/format.js';
import KpiCard from '../../components/KpiCard.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import DailySeriesChart from '../../components/usage/DailySeriesChart.jsx';

const mod = strings.admin.moderation;
const eng = strings.admin.homeEngagement;
const ft = strings.admin.forumTiles;

/** Sum a `{ date, count }` series (or an EnumerationResult of them). */
export function seriesTotal(series) {
  const list = Array.isArray(series) ? series : (series && Array.isArray(series.objects) ? series.objects : []);
  return list.reduce((sum, point) => sum + (Number(point && point.count) || 0), 0);
}

/** Open and overdue report counts (contract section 9.1), for administrators and moderators. */
export function ModerationTiles() {
  const { api } = useAdminAuth();
  const openFetcher = useCallback((signal) => api.listModerationQueue({ maxResults: 1, status: 'Open' }, signal), [api]);
  const overdueFetcher = useCallback((signal) => api.listModerationQueue({ maxResults: 1, status: 'Open', overdue: 'true' }, signal), [api]);
  const open = useApiQuery(openFetcher);
  const overdue = useApiQuery(overdueFetcher);
  const count = (q) => (q.data ? formatNumber(normalizePage(q.data, 1).totalCount) : '-');
  const overdueCount = overdue.data ? normalizePage(overdue.data, 1).totalCount : 0;
  return (
    <section className="panel" aria-labelledby="home-moderation-title">
      <h2 id="home-moderation-title">{mod.tileTitle}</h2>
      {(open.error || overdue.error) && <ErrorBanner message={mod.tileError} onRetry={() => { open.reload(); overdue.reload(); }} />}
      <div className="kpi-grid">
        <KpiCard label={mod.tileOpen} value={count(open)} to="/admin/moderation" />
        <KpiCard label={mod.tileOverdue} value={count(overdue)} tone={overdueCount ? 'danger' : undefined} to="/admin/moderation?overdue=true" />
      </div>
    </section>
  );
}

/**
 * The daily series of `GET /admin/metrics/engagement`: feed posts, comments, and reactions, plus
 * the forums series (`threadsByDay`, `threadCommentsByDay`, PHASE3_CONTRACT.md section 12) when
 * the server sends them.
 */
export function engagementSeries(d) {
  if (!d) return [];
  const out = [
    { key: 'posts', label: eng.posts, points: d.postsByDay },
    { key: 'comments', label: eng.comments, points: d.commentsByDay },
    { key: 'reactions', label: eng.reactions, points: d.reactionsByDay }
  ];
  if (Array.isArray(d.threadsByDay)) out.push({ key: 'threads', label: eng.threads, points: d.threadsByDay });
  if (Array.isArray(d.threadCommentsByDay)) out.push({ key: 'threadComments', label: eng.threadComments, points: d.threadCommentsByDay });
  return out;
}

/**
 * Posts, comments, reactions, active members, and scheduled posts in the last 30 days (section
 * 10.1), forum threads and forum comments when the server includes the forums series, and a chart
 * of every daily series with its data table.
 */
export function EngagementTiles() {
  const { api } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getEngagementMetrics(30, signal), [api]);
  const query = useApiQuery(fetcher);
  const d = query.data;
  const series = useMemo(() => engagementSeries(d), [d]);
  const forums = Boolean(d && Array.isArray(d.threadsByDay));
  return (
    <section className="panel" aria-labelledby="home-engagement-title">
      <h2 id="home-engagement-title">{eng.title}</h2>
      {query.error && <ErrorBanner message={eng.error} onRetry={query.reload} />}
      <div className="kpi-grid">
        <KpiCard label={eng.posts} value={d ? formatNumber(seriesTotal(d.postsByDay)) : '-'} to="/admin/feed" />
        <KpiCard label={eng.comments} value={d ? formatNumber(seriesTotal(d.commentsByDay)) : '-'} />
        <KpiCard label={eng.reactions} value={d ? formatNumber(seriesTotal(d.reactionsByDay)) : '-'} />
        <KpiCard label={eng.activeMembers} value={d ? formatNumber(d.activeMembers || 0) : '-'} />
        <KpiCard label={eng.scheduled} value={d ? formatNumber(d.scheduledPosts || 0) : '-'} to="/admin/feed?state=Scheduled" />
        {forums && <KpiCard label={eng.threads} value={formatNumber(seriesTotal(d.threadsByDay))} to="/admin/threads" />}
        {forums && Array.isArray(d.threadCommentsByDay) && <KpiCard label={eng.threadComments} value={formatNumber(seriesTotal(d.threadCommentsByDay))} />}
      </div>
      {d && <DailySeriesChart title={eng.chartTitle} series={series} type="line" />}
    </section>
  );
}

/** Forum activity in the last 30 days (PHASE3_CONTRACT.md section 12): threads, comments, votes, open review requests, and unanswered questions. */
export function ForumTiles() {
  const { client } = useAdminAuth();
  const fetcher = useCallback((signal) => loadDailyMetrics((q, sig) => client.adminForumMetrics(q, sig), { days: 30 }, signal), [client]);
  const query = useApiQuery(fetcher);
  const d = query.data;
  return (
    <section className="panel" aria-labelledby="home-forums-title">
      <h2 id="home-forums-title">{ft.title}</h2>
      {query.error && <ErrorBanner message={ft.error} onRetry={query.reload} />}
      <div className="kpi-grid">
        <KpiCard label={ft.threads} value={d ? formatNumber(seriesTotal(d.threadsByDay)) : '-'} to="/admin/threads" />
        <KpiCard label={ft.comments} value={d ? formatNumber(seriesTotal(d.commentsByDay)) : '-'} />
        <KpiCard label={ft.votes} value={d ? formatNumber(seriesTotal(d.votesByDay)) : '-'} />
        <KpiCard label={ft.activeThreads} value={d ? formatNumber(d.activeThreads || 0) : '-'} />
        <KpiCard label={ft.reviewRequests} value={d ? formatNumber(d.openReviewRequests || 0) : '-'} to="/admin/moderation?tab=reviewRequests" />
        <KpiCard label={ft.unanswered} value={d ? formatNumber(d.unansweredQuestions || 0) : '-'} />
      </div>
    </section>
  );
}

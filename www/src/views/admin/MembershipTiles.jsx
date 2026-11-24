import { useCallback } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import { formatNumber, formatPercent, formatUsd } from '../../utils/format.js';
import KpiCard from '../../components/KpiCard.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';

const h = strings.admin.homeMembership;
const en = strings.enums;

function Breakdown({ title, values, labels }) {
  const entries = Object.entries(values || {});
  if (!entries.length) return null;
  return (
    <div className="breakdown">
      <h3 className="label-text">{title}</h3>
      <dl className="kv-grid kv-grid-wide">
        {entries.map(([key, value]) => <div key={key} className="kv-item"><dt>{labels[key] || key}</dt><dd>{formatNumber(value)}</dd></div>)}
      </dl>
    </div>
  );
}

/** Admin Home tiles: membership metrics and the sign-up code summary. */
function MembershipTiles() {
  const { api } = useAdminAuth();
  const metricsFetcher = useCallback((signal) => api.getMembershipMetrics(signal), [api]);
  const codesFetcher = useCallback((signal) => api.getSignupCodeSummary(signal), [api]);
  const metrics = useApiQuery(metricsFetcher);
  const codes = useApiQuery(codesFetcher);
  const m = metrics.data;
  const sc = codes.data;
  const dash = (value, fmt = formatNumber) => (m ? fmt(value) : '-');
  const codeDash = (value, fmt = formatNumber) => (sc ? fmt(value) : '-');

  return (
    <>
      <section className="panel" aria-labelledby="home-membership-title">
        <div className="panel-header"><h2 id="home-membership-title">{h.title}</h2></div>
        {metrics.loading && !m && <Loading inline />}
        {metrics.error && <ErrorBanner message={h.error + ' ' + errorMessage(metrics.error, '')} onRetry={metrics.reload} />}
        <div className="kpi-grid">
          <KpiCard label={h.totalMembers} value={dash(m && m.totalMembers)} to="/admin/members" />
          <KpiCard label={h.activeMembers} value={dash(m && m.activeMembers)} to="/admin/members?membership=Active" tone="success" />
          <KpiCard label={h.paidActive} value={dash(m && m.paidActive)} to="/admin/members?membership=Paid" />
          <KpiCard label={h.freeActive} value={dash(m && m.freeActive)} to="/admin/members?membership=Free" />
          <KpiCard label={h.newMembers30d} value={dash(m && m.newMembers30d)} />
          <KpiCard label={h.lapsed30d} value={dash(m && m.lapsed30d)} tone={m && m.lapsed30d > 0 ? 'warning' : undefined} />
          <KpiCard label={h.estimatedArr} value={dash(m && m.estimatedArrUsd, formatUsd)} />
        </div>
        {m && (
          <div className="breakdown-grid">
            <Breakdown title={h.byStore} values={m.byStore} labels={en.store} />
            <Breakdown title={h.bySource} values={m.bySource} labels={en.membershipSource} />
          </div>
        )}
      </section>
      <section className="panel" aria-labelledby="home-codes-title">
        <div className="panel-header"><h2 id="home-codes-title">{h.codesTitle}</h2></div>
        {codes.loading && !sc && <Loading inline />}
        {codes.error && <ErrorBanner message={h.codesError + ' ' + errorMessage(codes.error, '')} onRetry={codes.reload} />}
        <div className="kpi-grid">
          <KpiCard label={h.activeCodes} value={codeDash(sc && sc.activeCodes)} to="/admin/signup-codes?status=Active" />
          <KpiCard label={h.totalRedemptions} value={codeDash(sc && sc.totalRedemptions)} />
          <KpiCard label={h.redemptions30d} value={codeDash(sc && sc.redemptions30d)} />
          <KpiCard label={h.activeFreeMembers} value={codeDash(sc && sc.activeFreeMembers)} />
          <KpiCard label={h.convertedToPaid} value={codeDash(sc && sc.convertedToPaid)} tone="success" />
          <KpiCard label={h.conversionRate} value={codeDash(sc && sc.conversionRate, (v) => formatPercent(v > 1 ? v / 100 : v))} />
        </div>
      </section>
    </>
  );
}

export default MembershipTiles;

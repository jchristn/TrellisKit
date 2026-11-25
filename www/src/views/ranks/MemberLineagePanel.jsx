/**
 * Lineage of another member on their profile, with Verify on entries that name the caller (the
 * flow a LineageVerificationRequested notification leads to). Renders nothing unless
 * `features.ranks` is on and the member has lineage.
 * Props: `userId`.
 */
import { useEffect, useMemo, useState } from 'react';
import { isRankStaff } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { clearMemberRankCache, loadMemberRank, useRanksFeature } from './rankHooks.js';
import { LineageList } from './LineageEditor.jsx';

const t = strings.ranks.lineage;

function MemberLineagePanel({ userId }) {
  const { client, me } = useAuth();
  const { enabled, rankFields } = useRanksFeature();
  const [entries, setEntries] = useState(null);
  useEffect(() => {
    if (!enabled || !client || !userId) return undefined;
    let live = true;
    loadMemberRank(client, userId).then((response) => { if (live) setEntries((response && response.lineage) || []); });
    return () => { live = false; };
  }, [enabled, client, userId]);
  const byField = useMemo(() => rankFields.map((field) => ({ field, list: (entries || []).filter((e) => e.fieldKey === field.key) })).filter((g) => g.list.length), [rankFields, entries]);
  if (!enabled || !byField.length) return null;
  const systemAdmin = Boolean(me && Array.isArray(me.roles) && me.roles.some((r) => (r && r.role) === 'SystemAdmin')) && isRankStaff(me);
  const verified = (entry) => {
    clearMemberRankCache();
    setEntries((list) => list.map((e) => (e.id === entry.id ? { ...e, ...entry, verified: true } : e)));
  };
  return (
    <section className="panel stack" aria-labelledby="member-lineage-title">
      <h2 id="member-lineage-title" className="section-title">{t.title}</h2>
      {byField.map(({ field, list }) => (
        <div key={field.key}>
          {byField.length > 1 && <h3 className="label-text">{field.label}</h3>}
          <LineageList entries={list} field={field} me={me} client={client} onVerified={verified} canVerifyAll={systemAdmin} />
        </div>
      ))}
    </section>
  );
}

export default MemberLineagePanel;

import { useCallback, useEffect, useMemo, useState } from 'react';
import { profileFieldsOf, rankBadgeMap, ranksFeatureEnabled, rankVerificationLabelsEnabled } from '@trelliskit/shared';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';

/**
 * Rank hooks for the member app. Everything is off unless the property reports `features.ranks`
 * (a missing switch counts as off); verification labels also need `features.rankVerificationLabels`.
 */

/** `{ enabled, labels, rankFields }` for the current property. */
export function useRanksFeature() {
  const { property } = useProperty();
  return useMemo(() => ({
    enabled: ranksFeatureEnabled(property),
    labels: rankVerificationLabelsEnabled(property),
    rankFields: profileFieldsOf(property).filter((f) => f.type === 'Rank'),
    property
  }), [property]);
}

/** GET /me/rank while ranks are on (`data` stays null otherwise). */
export function useMyRank(client, enabled = true) {
  const fetcher = useCallback((signal) => client.getMyRank(signal), [client]);
  return useApiQuery(fetcher, { enabled: Boolean(enabled && client) });
}

const CACHE_MS = 5 * 60 * 1000;
const cache = new Map();

/** Forget cached member ranks (after an award, or between tests). */
export function clearMemberRankCache() {
  cache.clear();
}

/** GET /members/{id}/rank once per member every five minutes, shared by every card on the page. */
export function loadMemberRank(client, userId) {
  const hit = cache.get(userId);
  if (hit && Date.now() - hit.at < CACHE_MS) return hit.promise;
  const promise = client.getMemberRank(userId).catch(() => null);
  cache.set(userId, { at: Date.now(), promise });
  return promise;
}

/**
 * Verification badges of a member's Rank fields (`{ [fieldKey]: 'verified' | 'selfReported' }`),
 * or an empty map when labels are off. `self` reads GET /me/rank instead.
 */
export function useMemberRankBadges(client, userId, { enabled = true } = {}) {
  const { labels } = useRanksFeature();
  const active = Boolean(labels && enabled && client && userId);
  const [badges, setBadges] = useState({});
  useEffect(() => {
    if (!active) return undefined;
    let live = true;
    loadMemberRank(client, userId).then((response) => { if (live) setBadges(rankBadgeMap(response)); });
    return () => { live = false; };
  }, [active, client, userId]);
  return active ? badges : {};
}

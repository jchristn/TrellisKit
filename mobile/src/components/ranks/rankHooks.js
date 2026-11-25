import { useEffect, useMemo, useState } from 'react';
import { profileFieldsOf, rankBadgeMap, ranksFeatureEnabled, rankVerificationLabelsEnabled } from '@trelliskit/shared';
import { useProperty } from '../../property';

/**
 * Rank hooks for the mobile app. Everything is off unless the property reports `features.ranks`
 * (a missing switch counts as off); verification labels also need `features.rankVerificationLabels`.
 */

/** `{ enabled, labels, rankFields }` for the current property. */
export function useRanksFeature() {
  const property = useProperty();
  return useMemo(() => ({
    enabled: ranksFeatureEnabled(property),
    labels: rankVerificationLabelsEnabled(property),
    rankFields: profileFieldsOf(property).filter((f) => f && f.type === 'Rank')
  }), [property]);
}

const CACHE_MS = 5 * 60 * 1000;
const cache = new Map();

/** Forget cached member ranks. */
export function clearMemberRankCache() {
  cache.clear();
}

/** GET /members/{id}/rank (or GET /me/rank for `self`) once per member every five minutes. */
export function loadMemberRank(api, userId, self = false) {
  const key = (self ? 'me:' : '') + userId;
  const hit = cache.get(key);
  if (hit && Date.now() - hit.at < CACHE_MS) return hit.promise;
  const promise = (self ? api.getMyRank() : api.getMemberRank(userId)).catch(() => null);
  cache.set(key, { at: Date.now(), promise });
  return promise;
}

/**
 * Verification badges of a member's Rank fields (`{ [fieldKey]: 'verified' | 'selfReported' }`);
 * empty while labels are off. `self` reads GET /me/rank.
 */
export function useMemberRankBadges(api, userId, { enabled = true, self = false } = {}) {
  const { labels } = useRanksFeature();
  const active = Boolean(labels && enabled && api && userId);
  const [badges, setBadges] = useState({});
  useEffect(() => {
    if (!active) return undefined;
    let live = true;
    loadMemberRank(api, userId, self).then((response) => { if (live) setBadges(rankBadgeMap(response)); });
    return () => { live = false; };
  }, [active, api, userId, self]);
  return active ? badges : {};
}

/** A short date ("Nov 25, 2026") for rank history and promoters. */
export function shortDate(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

/** A member summary's display name. */
export function memberName(user) {
  return (user && (user.displayName || user.handle || user.id)) || '';
}

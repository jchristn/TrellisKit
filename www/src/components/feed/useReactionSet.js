import { useMemo } from 'react';
import { reactionSet } from '@trelliskit/shared';
import { useProperty } from '../../context/PropertyContext.jsx';

/**
 * The property's reaction set (`GET /api/v1/property` reactions, merged over the build copy by
 * `useRuntimeProperty`): `{ items, likeKey }` in the property's order.
 */
export function useReactionSet() {
  const { property } = useProperty();
  return useMemo(() => reactionSet(property), [property]);
}

export default useReactionSet;

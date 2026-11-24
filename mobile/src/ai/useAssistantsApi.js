import { useMemo } from 'react';
import { useAuth } from '../auth/AuthProvider';
import { getVisitorClient } from './visitor';

/**
 * The client for assistant calls: the member's API client when signed in, else the visitor
 * client (src/ai/visitor.js) for Public assistants under anonymous browsing.
 * @returns {{ api: object, visitor: boolean }}
 */
export function useAssistantsApi() {
  const { api, user } = useAuth();
  return useMemo(() => (user ? { api, visitor: false } : { api: getVisitorClient(), visitor: true }), [api, user]);
}

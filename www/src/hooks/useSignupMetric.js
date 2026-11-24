import { useEffect, useRef } from 'react';

/** One anonymous signup-start count per mounted form, after live property consent loads. */
export default function useSignupMetric(client, property, runtimeLoaded, signedIn) {
  const sent = useRef(false);
  useEffect(() => {
    if (sent.current || signedIn || !runtimeLoaded || property?.productEventsEnabled !== true || !client?.post) return;
    sent.current = true;
    // Only a closed event and platform value cross the wire. Collection failures never affect signup.
    Promise.resolve().then(() => client.post('/metrics/events', { name: 'signup.started', dimensions: { source: 'web' } }, { retryOnUnauthorized: false })).catch(() => {});
  }, [client, property?.productEventsEnabled, runtimeLoaded, signedIn]);
}

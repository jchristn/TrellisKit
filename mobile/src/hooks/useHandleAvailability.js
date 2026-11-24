import { isHandle } from '@trelliskit/shared';
import { useEffect, useState } from 'react';
import { account } from '../api/endpoints';

/**
 * Status that can be decided without the server, or null when the server must be asked.
 * @returns {'idle'|'invalid'|'available'|null}
 */
function localStatus(value, currentHandle) {
  if (!value) return 'idle';
  if (!isHandle(value)) return 'invalid';
  if (currentHandle && value.toLowerCase() === currentHandle.toLowerCase()) return 'available';
  return null;
}

/**
 * Live handle availability with a short debounce.
 * @param {object} api MobileApiClient.
 * @param {string} handle Handle being typed.
 * @param {string|null} currentHandle The member's existing handle (always available to them).
 * @param {number} [delayMs]
 * @returns {'idle'|'invalid'|'checking'|'available'|'taken'|'error'}
 */
export function useHandleAvailability(api, handle, currentHandle, delayMs = 400) {
  const value = (handle || '').trim();
  const local = localStatus(value, currentHandle);
  const [result, setResult] = useState({ handle: null, status: null });

  useEffect(() => {
    if (local) return undefined;
    let active = true;
    const timer = setTimeout(() => {
      account
        .handleAvailability(api, value)
        .then((response) => {
          if (active) setResult({ handle: value, status: response && response.available ? 'available' : 'taken' });
        })
        .catch((error) => {
          if (active) setResult({ handle: value, status: error && error.status === 400 ? 'invalid' : 'error' });
        });
    }, delayMs);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [api, value, local, delayMs]);

  if (local) return local;
  return result.handle === value ? result.status : 'checking';
}

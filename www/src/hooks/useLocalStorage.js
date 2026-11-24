import { useCallback, useState } from 'react';

/**
 * useState backed by localStorage (JSON). Falls back to memory when storage is unavailable.
 * @template T
 * @param {string} key
 * @param {T} initialValue
 * @returns {[T, (value: T | ((prev: T) => T)) => void]}
 */
export function useLocalStorage(key, initialValue) {
  const [value, setValue] = useState(() => {
    try {
      const raw = window.localStorage.getItem(key);
      return raw === null ? initialValue : JSON.parse(raw);
    } catch {
      return initialValue;
    }
  });

  const update = useCallback((next) => {
    setValue((prev) => {
      const resolved = typeof next === 'function' ? next(prev) : next;
      try {
        window.localStorage.setItem(key, JSON.stringify(resolved));
      } catch {
        // Ignore storage failures.
      }
      return resolved;
    });
  }, [key]);

  return [value, update];
}

export default useLocalStorage;

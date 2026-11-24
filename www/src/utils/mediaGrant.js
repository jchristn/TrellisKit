/**
 * The page's media grant refresher, registered by `useMediaGrant`. Media elements call
 * `refreshMediaGrant()` after a 403 to renew the cookies once and retry (contract section 4.7).
 */
let refresher = null;

/** Register the refresher; returns an unregister function. */
export function setMediaGrantRefresher(fn) {
  refresher = fn;
  return () => {
    if (refresher === fn) refresher = null;
  };
}

/** Renew the media grant; resolves true when a refresher ran successfully. */
export function refreshMediaGrant() {
  return refresher ? refresher().then(() => true, () => false) : Promise.resolve(false);
}

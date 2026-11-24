import strings from '../strings.js';
import { clearPendingCode, getPendingCode, markPendingCodeAuto, setPendingCode } from './pendingCode.js';
import { setFlash } from './flash.js';
import { AGE_PATH, DOCUMENTS_PATH, MEMBERSHIP_PATH, safeNext } from './onboarding.js';
import { hasPendingDocuments, purchasesEnabled } from '@trelliskit/shared';
import { currentProperty } from '../property/current.js';

/**
 * Decide where to go after a sign-in, registration, social sign-in, or age attestation, and
 * redeem a pending sign-up code when appropriate (contract section 6):
 *
 * - New accounts (registration, or a first social sign-in) redeem a pending code automatically
 *   as soon as the age attestation is on file, and skip Stripe. A discount code cannot be redeemed;
 *   it goes to /app/membership, which offers it at checkout.
 * - Existing accounts with a pending code go to the redeem confirmation on /app/membership.
 * - Members with documents to accept (`Me.pendingDocuments`) review them first: code redemption
 *   is gated until they accept, and the review page calls this again afterwards.
 * - Everyone else continues to `next` (the member app guard then routes through onboarding).
 *
 * @param {{ me: object, isNewAccount?: boolean, api: object, setMe: Function, next?: string }} args
 * @returns {Promise<string>} The path to navigate to.
 */
export async function routeAfterAuth({ me, isNewAccount = false, api, setMe, next }) {
  if (isNewAccount) markPendingCodeAuto();
  const pending = getPendingCode();
  if (!me || !me.ageAttested) return AGE_PATH;
  if (hasPendingDocuments(me)) return DOCUMENTS_PATH + (next && safeNext(next) !== '/app' ? '?next=' + encodeURIComponent(safeNext(next)) : '');
  if (pending && !purchasesEnabled(currentProperty())) {
    // Sign-up codes are not available when nothing is sold (Open): drop the code and continue.
    clearPendingCode();
    return safeNext(next);
  }
  if (!pending) return safeNext(next);
  if (!pending.auto) return MEMBERSHIP_PATH + '?code=' + encodeURIComponent(pending.code);

  try {
    const result = await api.redeemCode(pending.code);
    clearPendingCode();
    if (result && result.membership) setMe((current) => (current ? { ...current, membership: result.membership } : current));
    setFlash('success', strings.onboarding.codeApplied((result && result.grantSummary) || pending.code));
    return safeNext(next);
  } catch (err) {
    // Keep the code for the confirmation form, but do not retry automatically. A discount code
    // is not redeemed: the membership page offers it at checkout.
    setPendingCode(pending.code, { auto: false });
    if (err && err.code === 'DiscountCodeAtCheckout') setFlash('info', strings.onboarding.codeIsDiscount);
    else setFlash('danger', strings.onboarding.codeFailed);
    return MEMBERSHIP_PATH + '?code=' + encodeURIComponent(pending.code);
  }
}

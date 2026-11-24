import { isAdult, isEmail } from '@trelliskit/shared';
import { parseDateParts } from './components/DateField';
import { joinTitles, separateDocuments, termsDocuments } from './documents/documents';
import { getBundledProperty, minimumAgeOf } from './property/property';
import strings from './strings';

/**
 * Client-side checks for registration. The server stays authoritative; these catch obvious
 * mistakes and enforce the property's minimum age (membership.minimumAge) before anything is sent.
 * Required documents: the terms checkbox (`acceptTerms`) covers the property's terms documents,
 * and each `RequiredAtSignup` document with `separateCheckbox` needs its own box
 * (`acceptedDocuments[key]`); a missing one is reported as `acceptedDocuments.<key>`, the same
 * key as the server's field error.
 * @param {{ displayName: string, email: string, password: string, dob: { month: string, day: string, year: string }, acceptTerms: boolean, acceptedDocuments?: Record<string, boolean> }} values
 * @param {Date} [today]
 * @param {object} [property] Defaults to the bundled property.
 * @returns {{ errors: Record<string, string>, dateOfBirth: string|null }}
 */
export function validateRegistration(values, today = new Date(), property = getBundledProperty()) {
  const e = strings.register.errors;
  const errors = {};
  const name = (values.displayName || '').trim();
  if (name.length < 2 || name.length > 60) errors.displayName = e.displayName;
  const email = (values.email || '').trim();
  if (!isEmail(email)) errors.email = e.email;
  const password = values.password || '';
  if (password.length < 10 || password.length > 128) errors.password = e.passwordLength;
  else if (email && password.toLowerCase() === email.toLowerCase()) errors.password = e.passwordEmail;
  const parsed = parseDateParts(values.dob || {});
  if (!parsed || parsed.date > today) errors.dateOfBirth = e.dateOfBirth;
  else if (!isAdult(parsed.date, today, minimumAgeOf(property))) errors.dateOfBirth = e.underage;
  if (!values.acceptTerms) {
    const covered = termsDocuments(property).map((d) => d.title || d.key);
    errors.acceptTerms = covered.length ? e.termsDocuments(joinTitles(covered)) : e.terms;
  }
  const checked = values.acceptedDocuments || {};
  for (const doc of separateDocuments(property)) {
    if (!checked[doc.key]) errors['acceptedDocuments.' + doc.key] = e.document(doc.title || doc.key);
  }
  return { errors, dateOfBirth: parsed ? parsed.iso : null };
}

/**
 * Check the documents step: every pending document must be checked. Errors use the server's
 * field error keys (`documents.<key>`).
 * @param {object[]} pending Pending documents.
 * @param {Record<string, boolean>} checked
 * @returns {Record<string, string>}
 */
export function validateDocumentAcceptance(pending, checked) {
  const errors = {};
  for (const doc of pending || []) {
    if (!(checked && checked[doc.key])) errors['documents.' + doc.key] = strings.documents.acceptRequired(doc.title || doc.key);
  }
  return errors;
}

/**
 * Validate a date of birth for age attestation.
 * @returns {{ error: string|null, dateOfBirth: string|null }}
 */
export function validateDateOfBirth(dob, today = new Date(), property = getBundledProperty()) {
  const parsed = parseDateParts(dob || {});
  if (!parsed || parsed.date > today) return { error: strings.register.errors.dateOfBirth, dateOfBirth: null };
  if (!isAdult(parsed.date, today, minimumAgeOf(property))) return { error: strings.register.errors.underage, dateOfBirth: parsed.iso };
  return { error: null, dateOfBirth: parsed.iso };
}

/** Password policy check shared by registration and password screens. */
export function passwordError(password, email) {
  if (!password || password.length < 10 || password.length > 128) return strings.register.errors.passwordLength;
  if (email && password.toLowerCase() === String(email).toLowerCase()) return strings.register.errors.passwordEmail;
  return null;
}

// Profile forms are validated from the property's profile field definitions: see
// src/profile/fields.js (built-ins) and validateProfileValues in @trelliskit/shared.

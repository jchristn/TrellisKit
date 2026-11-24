import { useRouter, useSegments } from 'expo-router';
import { useEffect } from 'react';
import { useAuth } from '../auth/AuthProvider';
import { Routes } from '../auth/routing';
import { hasPendingDocuments } from '../documents/documents';

/**
 * Sections that stay open while documents are pending: the entry route, sign-in and setup
 * steps, the document reader, Account, and support (the server leaves account routes open too).
 * The tabs send the member on by themselves (canEnterTabs).
 */
const OPEN_SECTIONS = ['(onboarding)', '(setup)', '(tabs)', 'documents', 'account', 'support'];

/**
 * Sends a signed-in, age-attested member with pending documents (`Me.pendingDocuments`, for
 * example after a `403 DocumentAcceptanceRequired` reloaded Me) from any other screen to the
 * documents step. Renders nothing.
 */
export default function DocumentsGate() {
  const { session } = useAuth();
  const router = useRouter();
  const segments = useSegments();
  const section = segments[0] || '';
  const user = session.status === 'signedIn' ? session.user : null;
  const blocked = Boolean(user && user.ageAttested && hasPendingDocuments(user)) && section !== '' && !OPEN_SECTIONS.includes(section);
  useEffect(() => {
    if (blocked) router.replace(Routes.documents);
  }, [blocked, router]);
  return null;
}

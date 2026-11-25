import { useAuth } from '../../auth/AuthProvider';
import VenueMedia from '../places/VenueMedia';

/** Owner-managed profile photos and videos, with per-item reporting. */
export default function ProfileMedia({ user, initialReportId = null }) {
  const { user: me } = useAuth();
  if (!user) return null;
  return <VenueMedia key={user.id} profile initialReportId={initialReportId} venue={{ id: user.id, viewer: { canAddMedia: me?.id === user.id }, photos: [] }} testID="profile-media" />;
}

import { useAuth } from '../../context/AuthContext.jsx';
import { VenueMediaSection } from '../directory/VenueMedia.jsx';

/** Reuse the upload, gallery, and per-item report controls for a profile owner. */
export default function ProfileMedia({ user }) {
  const { me } = useAuth();
  return <VenueMediaSection key={user.id} profile venue={{ id: user.id, viewer: { canAddMedia: me?.id === user.id }, photos: [] }} />;
}

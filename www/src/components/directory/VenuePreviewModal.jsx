import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { formatAddress, isLockedItem, programLabels } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import Modal from '../Modal.jsx';
import { Badge } from '../Badges.jsx';
import { ErrorBanner, Loading } from '../States.jsx';
import { RatingBadge } from './Stars.jsx';
import { VenueSchedule } from './VenueSchedule.jsx';
import { errorText } from '../../utils/engagement.js';
import { appPaths, phase4Errors } from '../../utils/phase4.js';

const v = strings.venues;

/**
 * The preview that opens when a place is picked on the map: a small dialog with the essentials
 * (what it is, where, how to reach it, opening hours, visitors) and a link to the full page. The
 * venue is read when the dialog opens, so a slow answer shows a spinner under the name-less title.
 * @param {{ client: object, venueId: string|null, onClose: () => void }} props
 */
function VenuePreviewModal({ client, venueId, onClose }) {
  const { property } = useProperty();
  const [state, setState] = useState({ id: null, venue: null, error: null });

  useEffect(() => {
    if (!venueId) return undefined;
    const controller = new AbortController();
    client.getVenue(venueId, controller.signal).then(
      (venue) => { if (!controller.signal.aborted) setState({ id: venueId, venue, error: null }); },
      (error) => { if (!controller.signal.aborted) setState({ id: venueId, venue: null, error }); }
    );
    return () => controller.abort();
  }, [client, venueId]);

  if (!venueId) return null;
  const current = state.id === venueId;
  const venue = current ? state.venue : null;
  const error = current ? state.error : null;
  const locked = venue ? isLockedItem(venue) : false;
  const address = venue ? formatAddress(venue.address, venue.addressVisibility, { includeCountry: false }) : '';
  const programs = venue ? programLabels(property, venue.programs) : [];
  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title={venue ? venue.name : v.previewTitle}
      subtitle={venue && venue.affiliation ? venue.affiliation : undefined}
      footer={venue ? <Link to={appPaths.venue(venue.slug || venue.id)} className="button button-primary" data-testid="venue-preview-open">{v.previewOpen}</Link> : undefined}
    >
      <div className="venue-preview" data-testid="venue-preview">
        {!venue && !error && <Loading />}
        <ErrorBanner message={error ? errorText(error, phase4Errors(), v.previewError) : ''} />
        {venue && (
          <>
            <span className="badge-row">
              {venue.isVerified && <Badge tone="success">{v.verified}</Badge>}
              {venue.status === 'Closed' && <Badge tone="danger">{v.closed}</Badge>}
              {venue.visitorPolicy && venue.visitorPolicy !== 'Unknown' && <Badge tone="neutral">{v.visitorPolicies[venue.visitorPolicy]}</Badge>}
              {venue.ratingCount > 0 && <RatingBadge average={venue.ratingAverage} count={venue.ratingCount} />}
            </span>
            {programs.length > 0 && <p className="muted">{programs.join(', ')}</p>}
            {venue.summary && <p>{venue.summary}</p>}
            {address && <p>{address}</p>}
            {!locked && (venue.phone || venue.websiteUrl) && (
              <p className="venue-preview-contact">
                {venue.phone && <a href={'tel:' + venue.phone.replace(/[^+\d]/g, '')}>{venue.phone}</a>}
                {venue.websiteUrl && <a href={venue.websiteUrl} target="_blank" rel="noopener noreferrer">{v.website}</a>}
              </p>
            )}
            {!locked && (venue.schedule || venue.scheduleText) && <VenueSchedule schedule={venue.schedule} text={venue.scheduleText} hoursOnly />}
          </>
        )}
      </div>
    </Modal>
  );
}

export default VenuePreviewModal;

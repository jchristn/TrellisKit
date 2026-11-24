import { MapProvider, mobileMapProvider } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { View } from 'react-native';
import { useLayout } from '../../hooks/useLayout';
import { mapModule } from '../../native/maps';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import SplitView from '../SplitView';
import EventDetail from './EventDetail';
import EventList from './EventList';
import EventsMapSearch from './EventsMapSearch';

/**
 * Events on the Map tab. With a map (the directory's map provider) the search is map first
 * (EventsMapSearch). Without one, or when the map cannot load, it is the list, and on tablets the
 * selected event shows beside it in SplitView; phones open `/events/{id}`.
 */
export default function EventsPane({ inset = false, testID = 'screen-events' }) {
  const property = useProperty();
  const provider = mobileMapProvider(property);
  const [mapFailed, setMapFailed] = useState(() => provider === MapProvider.None || !mapModule(provider));
  const unavailable = useCallback(() => setMapFailed(true), []);
  if (!mapFailed) return <EventsMapSearch provider={provider} onUnavailable={unavailable} inset={inset} testID={testID} />;
  return <EventsSplit testID={testID} />;
}

function EventsSplit({ testID }) {
  const theme = useTheme();
  const router = useRouter();
  const { isWide } = useLayout();
  const [eventId, setEventId] = useState(null);
  const open = (event) => (isWide ? setEventId(event.id) : router.push('/events/' + event.id));
  return (
    <SplitView
      testID={testID}
      scroll={false}
      list={<EventList onOpen={open} selectedId={eventId} />}
      detail={
        eventId ? (
          <EventDetail key={eventId} eventId={eventId} />
        ) : (
          <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.spacing.xl }} testID="events-select">
            <AppText color="textMuted">{strings.events.selectEvent}</AppText>
          </View>
        )
      }
    />
  );
}

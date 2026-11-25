import { useLocalSearchParams } from 'expo-router';
import MemberGate from '../src/components/MemberGate';
import CheckInScreen from '../src/components/ranks/CheckInScreen';

/**
 * `/check-in?t=&id=&d=&c=`: attendance check-in opened from a scanned QR code (the web domain's
 * `/app/check-in?...` universal link maps here) or from code entry.
 */
export default function CheckInRoute() {
  const params = useLocalSearchParams();
  return (
    <MemberGate>
      <CheckInScreen key={JSON.stringify(params)} params={params} />
    </MemberGate>
  );
}

import { formatRelativeTime } from '@trelliskit/shared';
import { Pressable, View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';

const t = strings.support;

/** The status text of a ticket. */
export function statusText(status) {
  return t.statuses[status] || status || '';
}

/** One ticket in the list: number, title, type, status, and last activity. */
export default function TicketRow({ ticket, onOpen, selected = false }) {
  const theme = useTheme();
  const status = statusText(ticket.status);
  const type = ticket.type ? ticket.type.label || ticket.type.key || ticket.type : '';
  return (
    <Pressable
      onPress={() => onOpen(ticket)}
      accessibilityRole="button"
      accessibilityLabel={t.rowLabel(ticket.number, ticket.title, status)}
      accessibilityState={{ selected }}
      testID={'ticket-' + ticket.id}
      style={({ pressed }) => ({ paddingVertical: theme.spacing.md, paddingHorizontal: theme.spacing.sm, gap: theme.spacing.xxs, borderRadius: theme.radii.md, backgroundColor: selected ? theme.colors.surface : 'transparent', opacity: pressed ? 0.7 : 1 })}
    >
      <View style={{ flexDirection: 'row', gap: theme.spacing.sm, alignItems: 'center' }}>
        <AppText variant="caption" color="textMuted" style={{ flex: 1 }}>
          {t.ticketNumber(ticket.number) + (type ? ' · ' + type : '')}
        </AppText>
        <AppText variant="caption" color={ticket.status === 'WaitingOnMember' ? 'warning' : 'textMuted'} style={{ fontWeight: '700' }} testID={'ticket-' + ticket.id + '-status'}>
          {status}
        </AppText>
      </View>
      <AppText variant="label">
        {ticket.title}
      </AppText>
      {ticket.lastActivityUtc ? (
        <AppText variant="caption" color="textMuted">
          {formatRelativeTime(ticket.lastActivityUtc)}
        </AppText>
      ) : null}
    </Pressable>
  );
}

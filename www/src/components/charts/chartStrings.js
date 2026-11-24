/** Shared chart copy, exposed through the website string catalog as chartKit. */
const chartKit = {
  requestFilters: { routeTemplate: 'Route template', statusClass: 'Status class', errorCode: 'Error code', clientApp: 'Client app', clientPlatform: 'Client platform', clientVersion: 'Client version' },
  statusClassInvalid: 'Use a status class from 1xx through 5xx.',
  annotationKind: 'Event kind', allEvents: 'All events',
  started: 'Started', ended: 'Ended', outcome: 'Outcome', interval: 'Interval (seconds)', error: 'Error type', recentPasses: 'Timeline shows up to 2,000 passes from the last 31 days of this range.',
  showTable: 'Show as table', hideTable: 'Hide table', download: 'Download CSV',
  noData: 'No observations in this period.', missing: 'No data', suppressed: 'Fewer than 5', partial: 'Partial', complete: 'Complete',
  series: 'Series', time: 'Time', value: 'Value', status: 'Status', previous: 'Previous period',
  range: 'Range', ranges: { hour: '1 hour', day: '24 hours', week: '7 days', month: '30 days', quarter: '90 days', custom: 'Custom' },
  from: 'From', to: 'To', compare: 'Compare previous period', refresh: 'Refresh', group: 'Group by', all: 'No grouping',
  filter: 'Filter dimension', filterValue: 'Filter value', apply: 'Apply filter', clear: 'Clear filters',
  completeThrough: 'Complete through', timezone: 'Time zone', local: 'Local', current: 'Latest', change: 'Change', unavailable: 'Unavailable',
  unknownPage: 'This metrics page is not available.', loadError: 'Metrics could not be loaded.', invalidRange: 'Choose an end time after the start time.',
  title: 'Metrics', summary: 'Health, growth, engagement, and background activity.', details: 'View records',
  health: 'Health', growth: 'Growth', workers: 'Workers', worker: 'Worker', duration: 'Duration', items: 'Items processed',
  lag: 'Since last success', noSuccess: 'No successful pass recorded', seconds: 'seconds', annotations: 'Events',
  lastValue: (key, value, unit) => `${key}: latest ${value}${unit ? ' ' + unit : ''}.`,
  periodChange: (value) => `Change against the previous period: ${value}.`,
  knownPoints: (count) => `${count} observed values. Missing observations remain gaps.`,
};

export default chartKit;

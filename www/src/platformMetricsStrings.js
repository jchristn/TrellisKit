const platformMetrics = {
  title: 'Platform health', summary: 'Compare a registered metric across properties and inspect retained history.',
  metric: 'Metric key', search: 'Property search', ordering: 'Order', descending: 'Highest first', ascending: 'Lowest first',
  property: 'Property', value: 'Value', aggregation: 'Aggregation', completeness: 'Completeness', complete: 'Complete', partial: 'Partial', missing: 'No observations',
  note: 'Counts are summed; gauges use the last bucket; distributions show the worst bucket percentile. Missing and suppressed data remain gaps. Charts show up to 20 properties per page in UTC.',
  comparisons: 'Property comparison', series: 'Property trends', refresh: 'Refresh', all: 'All statuses', enabled: 'Enabled', disabled: 'Disabled', status: 'Property status',
  invalid: 'Choose a valid range of at most 366 days.', error: 'Unable to load property metrics.'
};

export default platformMetrics;

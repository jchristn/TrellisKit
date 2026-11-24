# Chart kit

Import components from `./index.js`. The kit contains `TimeSeries`, `RankedBars`, `Funnel`, `CohortGrid`, `HeatMap`, `Histogram`, `UptimeStrip`, `Swimlanes`, `StatTile`, `RangePicker`, and `ChartFrame`.

```jsx
<TimeSeries title="Requests" type="stackedArea" unit="count"
  series={response.series} previous={response.previous}
  annotations={response.annotations} timeZone={response.timeZone}
  completeThroughUtc={response.completeThroughUtc}
  onPointClick={(point, series) => openRecords(point.startUtc, point.endUtc, series.key)} />
```

Series contain `key`, optional `label` and `axis` (`left` or `right`), and `points`. Each point contains `startUtc`, optional `endUtc`, `value`, `complete`, and `suppressed`. A non-finite, missing, or suppressed value is never plotted or exported as a number. Missing buckets break paths; partial buckets stay visible at reduced opacity. The server owns completeness and privacy decisions. Time tooltips show the property time zone and local time; exact UTC boundaries remain in CSV.

Every plotted chart has a textual summary, an accessible table, and CSV export. Tables provide a keyboard alternative to pointer drill-down. Comparison groups are matched by key. CSV cells neutralize spreadsheet formula prefixes. Charts bound their input to 21 groups and 2,000 points per group. Large diagrams scroll within their card on small screens to keep labels readable; tables remain available. Colors derive from the property theme. Lines alternate dash and marker styles, and worker failures use an outline as well as color.

`TimeSeries.type` accepts `line`, `area`, `stackedArea`, `bars`, and `stackedBars`. Signed values and separate axes are supported. `threshold` draws a reference line. `RankedBars` accepts series or `rows` with `key`, `label`, `value`, optional `secondary`, and an optional local admin `href`. `Histogram` preserves declared bin order, including one series with labeled bin points. `Swimlanes` uses exact interval boundaries and accepts `outcome` on points. `StatTile` accepts a value, previous value, optional sparkline points, and a detail link.

Admin pages consume declarations from `/admin/metrics/pages` and definitions from `/admin/metrics`. A property adds metrics and pages through its metrics configuration; the same generic route, sidebar, filters, table, and export render them. See `docs/METRICS_CONTRACT.md` for backend configuration. The current range, compare toggle, grouping, and declared filters stay in the URL. Feature-disabled charts are omitted.

Source drilldowns are enabled only for exact contracts: retained requests (including route/status/error/client dimensions), thread creation by forum, and moderation actions. Unsupported snapshot/source/grouping links stay inactive. `canPointClick(point, series)` can additionally disable individual chart and table controls; suppressed and missing values are never drill-down controls.

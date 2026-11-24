import { useEffect, useRef, useState } from 'react';
import uPlot from 'uplot';
import 'uplot/dist/uPlot.min.css';
import { formatMeterValue } from '../../utils/usage.js';

const FALLBACK_SERIES = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300'];
const DEFAULT_HEIGHT = 200;
const DEFAULT_WIDTH = 600;

function cssVar(name, fallback) {
  if (typeof window === 'undefined' || !window.getComputedStyle) return fallback;
  const value = window.getComputedStyle(document.documentElement).getPropertyValue(name);
  return (value && value.trim()) || fallback;
}

/** A hex color (`#rgb` or `#rrggbb`) with alpha; other color strings are returned as is. */
export function withAlpha(color, alpha) {
  const m = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(String(color || '').trim());
  if (!m) return color;
  const hex = m[1].length === 3 ? m[1].split('').map((c) => c + c).join('') : m[1];
  const n = parseInt(hex, 16);
  return 'rgba(' + ((n >> 16) & 255) + ', ' + ((n >> 8) & 255) + ', ' + (n & 255) + ', ' + alpha + ')';
}

/** Theme palette read from CSS custom properties, so light and dark themes both apply. */
function readPalette() {
  return {
    series: FALLBACK_SERIES.map((fallback, i) => cssVar('--usage-series-' + (i + 1), fallback)),
    axis: cssVar('--color-text-muted', '#576274'),
    grid: cssVar('--color-border', '#dde2ea'),
    surface: cssVar('--color-surface', '#ffffff')
  };
}

/** Re-render when the document theme (`<html data-theme>`) changes. */
function useThemeVersion() {
  const [version, setVersion] = useState(0);
  useEffect(() => {
    if (typeof MutationObserver === 'undefined') return undefined;
    const observer = new MutationObserver(() => setVersion((v) => v + 1));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    return () => observer.disconnect();
  }, []);
  return version;
}

function bucketStep(xs) {
  for (let i = 1; i < xs.length; i += 1) {
    const step = xs[i] - xs[i - 1];
    if (step > 0) return step;
  }
  return 86400;
}

/**
 * The one uPlot wrapper. `type` is `line` or `area` (gauges, totals over time), `bars` (counts
 * per bucket), or `stacked` (an area per layer; `data` holds cumulative values bottom to top and
 * `layers` the raw values shown in the legend). The canvas is decorative: callers wrap it in an
 * element with `role="img"` and a text alternative, and offer a data table.
 *
 * @param {{ data: Array<Array<number|null>>, series: Array<{ label: string }>, type: 'line'|'area'|'bars'|'stacked',
 *   unit?: string, layers?: Array<Array<number|null>>, timeZone?: string, height?: number }} props
 */
function UsageChart({ data, series, type = 'line', unit = 'count', layers = null, timeZone = 'UTC', height = DEFAULT_HEIGHT }) {
  const hostRef = useRef(null);
  const plotRef = useRef(null);
  const theme = useThemeVersion();

  useEffect(() => {
    const host = hostRef.current;
    if (!host || !data || !data.length) return undefined;
    const palette = readPalette();
    const fmt = (v) => (v === null || v === undefined ? '-' : formatMeterValue(v, unit));
    const xs = data[0] || [];
    const half = bucketStep(xs) / 2;
    const stacked = type === 'stacked';
    const bars = type === 'bars' && uPlot.paths && uPlot.paths.bars ? uPlot.paths.bars({ size: [0.7, 64], gap: 2 }) : undefined;

    // Stacked layers are drawn top first so each lower layer paints over the one above it.
    const order = series.map((_, i) => i);
    if (stacked) order.reverse();
    const seriesOpts = order.map((i) => {
      const color = palette.series[i % palette.series.length];
      const opt = {
        label: series[i].label,
        stroke: color,
        width: type === 'bars' ? 0 : 2,
        points: { show: false },
        value: (u, v, _s, idx) => (stacked && layers && idx !== null && idx !== undefined ? fmt(layers[i][idx]) : fmt(v))
      };
      if (type === 'bars') {
        opt.paths = bars;
        opt.fill = color;
        opt.width = 0;
      } else if (type === 'area') opt.fill = withAlpha(color, 0.16);
      else if (stacked) opt.fill = withAlpha(color, 0.85);
      return opt;
    });
    const plotData = [xs, ...order.map((i) => data[i + 1] || [])];

    const tzDate = uPlot.tzDate ? (ts) => uPlot.tzDate(new Date(ts * 1000), timeZone) : undefined;
    const axis = { stroke: palette.axis, grid: { stroke: palette.grid, width: 1 }, ticks: { stroke: palette.grid, width: 1 } };
    const opts = {
      width: host.clientWidth || DEFAULT_WIDTH,
      height,
      tzDate,
      legend: { show: true, live: true },
      cursor: { points: { size: 8, fill: palette.surface } },
      scales: {
        x: { time: true, range: type === 'bars' ? (u, min, max) => [min - half, max + half] : undefined },
        y: { range: (u, min, max) => [Math.min(0, min || 0), max > 0 ? max * 1.1 : 1] }
      },
      axes: [
        { ...axis },
        { ...axis, size: 64, values: (u, splits) => splits.map(fmt) }
      ],
      series: [{ value: (u, v) => (v === null || v === undefined ? '-' : new Date(v * 1000).toISOString().slice(0, 16).replace('T', ' ')) }, ...seriesOpts]
    };
    if (tzDate) {
      opts.series[0].value = (u, v) => {
        if (v === null || v === undefined) return '-';
        const d = tzDate(v);
        return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0') + ' ' + String(d.getHours()).padStart(2, '0') + ':00';
      };
    }

    const plot = new uPlot(opts, plotData, host);
    plotRef.current = plot;
    let observer = null;
    if (typeof ResizeObserver !== 'undefined') {
      observer = new ResizeObserver(() => {
        const width = host.clientWidth;
        if (width > 0 && plotRef.current) plotRef.current.setSize({ width, height });
      });
      observer.observe(host);
    }
    return () => {
      if (observer) observer.disconnect();
      plot.destroy();
      plotRef.current = null;
    };
  }, [data, series, type, unit, layers, timeZone, height, theme]);

  return <div ref={hostRef} className="usage-chart-canvas" aria-hidden="true" />;
}

export default UsageChart;

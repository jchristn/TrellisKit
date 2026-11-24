/**
 * A small WebVTT reader for lesson captions (docs/PHASE4_CONTRACT.md section 5.4). The player
 * shows the active cue as text over the video, so captions work with any stream the player can
 * play. Cue settings, styles, regions, and notes are ignored; tags such as `<b>` or `<v Name>` are
 * stripped. At most `maxCues` cues are kept.
 */

function seconds(stamp) {
  const m = /^(?:(\d+):)?(\d{1,2}):(\d{2})[.,](\d{1,3})$/.exec(String(stamp).trim());
  if (!m) return null;
  return Number(m[1] || 0) * 3600 + Number(m[2]) * 60 + Number(m[3]) + Number(m[4].padEnd(3, '0')) / 1000;
}

/**
 * Parse WebVTT text into cues sorted by start.
 * @param {string} text
 * @param {{ maxCues?: number }} [options]
 * @returns {{ start: number, end: number, text: string }[]}
 */
export function parseVtt(text, { maxCues = 5000 } = {}) {
  const source = String(text || '').replace(/^﻿/, '').replace(/\r\n?/g, '\n');
  if (!/^WEBVTT/.test(source)) return [];
  const cues = [];
  for (const block of source.split(/\n{2,}/)) {
    const lines = block.split('\n').filter((l) => l.length);
    const timing = lines.findIndex((l) => l.includes('-->'));
    if (timing < 0) continue;
    const [from, rest] = lines[timing].split('-->');
    const start = seconds(from);
    const end = seconds(String(rest || '').trim().split(/\s+/)[0]);
    if (start === null || end === null || end <= start) continue;
    const body = lines
      .slice(timing + 1)
      .join('\n')
      .replace(/<[^>]*>/g, '')
      .replace(/&amp;/g, '&')
      .replace(/&lt;/g, '<')
      .replace(/&gt;/g, '>')
      .replace(/&nbsp;/g, ' ')
      .trim();
    if (body) cues.push({ start, end, text: body });
    if (cues.length >= maxCues) break;
  }
  return cues.sort((a, b) => a.start - b.start);
}

/** The text of the cues active at `time` (joined by a line break), or ''. */
export function cueTextAt(cues, time) {
  const t = Number(time) || 0;
  const active = [];
  for (const cue of cues || []) {
    if (cue.start > t) break;
    if (cue.end > t) active.push(cue.text);
  }
  return active.join('\n');
}
